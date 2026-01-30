using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VK.SequenceSystem.Core
{
    public class SequenceManager : MonoBehaviour
    {
        private SequenceData[] _sequences;
        private bool[] _isRunning;

        private HashSet<ActiveSequence> _activeSequenceSet = new HashSet<ActiveSequence>();
        private List<ActiveSequence> _activeSequences = new List<ActiveSequence>(16);
        private readonly Queue<ActiveSequence> _pool = new Queue<ActiveSequence>();

        private Dictionary<int, List<ActiveSequence>> _eventToWaitingSequences =
            new Dictionary<int, List<ActiveSequence>>(32);

        private Dictionary<ActiveSequence, List<int>> _sequenceToWaitingEvents =
            new Dictionary<ActiveSequence, List<int>>(32);

        private Dictionary<int, int> _sequenceToIndex = new Dictionary<int, int>(32);
        private Dictionary<int, Action<EventData>> _eventSubscriptions = new Dictionary<int, Action<EventData>>(32);

        private class ActiveSequence
        {
            public int SequenceId;
            public int CurrentStep;
            public bool IsWaiting;
            public Coroutine DelayCoroutine;
        }

        void Awake()
        {
            const int INITIAL_CAPACITY = 128;
            _sequences = new SequenceData[INITIAL_CAPACITY];
            _isRunning = new bool[INITIAL_CAPACITY];
        }

        void OnDestroy()
        {
            StopAllSequences();
            UnsubscribeFromAllEvents();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterSequence(int sequenceId, SequenceData data)
        {
            EnsureCapacity(sequenceId);

            if (data.IsValid)
            {
                data.Validate();
                _sequences[sequenceId] = data;
            }
            else
            {
                Debug.LogError($"Invalid sequence data for ID: {sequenceId}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StartSequence(int sequenceId)
        {
            if (sequenceId >= _sequences.Length || !_sequences[sequenceId].IsValid || _isRunning[sequenceId])
                return;

            var seq = GetOrCreateActiveSequence();
            seq.SequenceId = sequenceId;
            seq.CurrentStep = 0;
            seq.IsWaiting = false;
            seq.DelayCoroutine = null;

            _activeSequences.Add(seq);
            _activeSequenceSet.Add(seq);
            _sequenceToIndex[sequenceId] = _activeSequences.Count - 1;
            _isRunning[sequenceId] = true;

            ExecuteCurrentStep(seq);
        }

        public void StopSequence(int sequenceId)
        {
            if (_sequenceToIndex.TryGetValue(sequenceId, out int index) &&
                index < _activeSequences.Count &&
                _activeSequences[index].SequenceId == sequenceId)
            {
                var seq = _activeSequences[index];
                CleanupSequence(seq);
                ReturnToPool(seq);
                _activeSequenceSet.Remove(seq);

                int lastIndex = _activeSequences.Count - 1;
                if (index != lastIndex)
                {
                    _activeSequences[index] = _activeSequences[lastIndex];
                    _sequenceToIndex[_activeSequences[index].SequenceId] = index;
                }

                _activeSequences.RemoveAt(lastIndex);
                _sequenceToIndex.Remove(sequenceId);
                _isRunning[sequenceId] = false;
            }
            else
            {
                // fallback linear search
                for (int i = 0; i < _activeSequences.Count; i++)
                {
                    if (_activeSequences[i].SequenceId == sequenceId)
                    {
                        var seq = _activeSequences[i];
                        CleanupSequence(seq);
                        ReturnToPool(seq);
                        _activeSequenceSet.Remove(seq);

                        int lastIndex = _activeSequences.Count - 1;
                        if (i != lastIndex)
                        {
                            _activeSequences[i] = _activeSequences[lastIndex];
                            _sequenceToIndex[_activeSequences[i].SequenceId] = i;
                        }

                        _activeSequences.RemoveAt(lastIndex);
                        _sequenceToIndex.Remove(sequenceId);
                        _isRunning[sequenceId] = false;
                        break;
                    }
                }
            }
        }

        public void StopAllSequences()
        {
            foreach (var seq in _activeSequences)
            {
                CleanupSequence(seq);
                ReturnToPool(seq);
            }

            _activeSequences.Clear();
            _activeSequenceSet.Clear();
            Array.Clear(_isRunning, 0, _isRunning.Length);
            _eventToWaitingSequences.Clear();
            _sequenceToWaitingEvents.Clear();
            _sequenceToIndex.Clear();
            UnsubscribeFromAllEvents();
        }

        private void ExecuteCurrentStep(ActiveSequence seq)
        {
            var data = _sequences[seq.SequenceId];
            if (seq.CurrentStep >= data.ActionTypes.Length)
            {
                CompleteSequence(seq);
                return;
            }

            var actionType = data.ActionTypes[seq.CurrentStep];
            if (actionType == SequenceActionType.SingleEvent)
                ExecuteSingleEvent(data, seq);
            else if (actionType == SequenceActionType.ParallelEvents)
                ExecuteParallelEvents(data, seq);
            else if (actionType == SequenceActionType.WaitForEvent)
                ExecuteWaitForEvent(data, seq);
            else
                ExecuteDelay(data, seq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteSingleEvent(SequenceData data, ActiveSequence seq)
        {
            CheckPostExecution(data, seq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteParallelEvents(SequenceData data, ActiveSequence seq)
        {
            CheckPostExecution(data, seq);
        }

        private void ExecuteWaitForEvent(SequenceData data, ActiveSequence seq)
        {
            var waitStep = data.WaitSteps[seq.CurrentStep];
            int waitEventId = waitStep.WaitEventId;

            if (waitEventId != -1)
            {
                seq.IsWaiting = true;
                SubscribeToEvent(waitEventId, seq);
                if (!_sequenceToWaitingEvents.TryGetValue(seq, out var list))
                {
                    list = new List<int>(4);
                    _sequenceToWaitingEvents[seq] = list;
                }

                list.Add(waitEventId);
            }
            else
            {
                AdvanceToNextStep(seq);
            }
        }

        private void ExecuteDelay(SequenceData data, ActiveSequence seq)
        {
            float delay = data.Delays[seq.CurrentStep];
            if (delay > 0 && delay <= 3600f)
            {
                seq.IsWaiting = true;
                seq.DelayCoroutine = StartCoroutine(DelayCoroutine(seq, delay));
            }
            else
            {
                AdvanceToNextStep(seq);
            }
        }

        private System.Collections.IEnumerator DelayCoroutine(ActiveSequence seq, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_activeSequenceSet.Contains(seq) && seq.IsWaiting)
            {
                seq.IsWaiting = false;
                seq.DelayCoroutine = null;

                var data = _sequences[seq.SequenceId];
                var waitStep = data.WaitSteps[seq.CurrentStep];
                if (waitStep.WaitEventId != -1)
                    ExecuteWaitForEvent(data, seq);
                else
                    AdvanceToNextStep(seq);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckPostExecution(SequenceData data, ActiveSequence seq)
        {
            var waitStep = data.WaitSteps[seq.CurrentStep];
            if (waitStep.WaitEventId != -1)
            {
                ExecuteWaitForEvent(data, seq);
                return;
            }

            float delay = data.Delays[seq.CurrentStep];
            if (delay > 0)
            {
                ExecuteDelay(data, seq);
                return;
            }

            AdvanceToNextStep(seq);
        }

        private void AdvanceToNextStep(ActiveSequence seq)
        {
            seq.CurrentStep++;
            seq.IsWaiting = false;
            if (seq.DelayCoroutine != null)
            {
                StopCoroutine(seq.DelayCoroutine);
                seq.DelayCoroutine = null;
            }

            ExecuteCurrentStep(seq);
        }

        private void CompleteSequence(ActiveSequence seq)
        {
            CleanupSequence(seq);
            ReturnToPool(seq);
            _activeSequenceSet.Remove(seq);

            if (_sequenceToIndex.TryGetValue(seq.SequenceId, out int index))
            {
                int lastIndex = _activeSequences.Count - 1;
                if (index != lastIndex)
                {
                    _activeSequences[index] = _activeSequences[lastIndex];
                    _sequenceToIndex[_activeSequences[index].SequenceId] = index;
                }

                _activeSequences.RemoveAt(lastIndex);
                _sequenceToIndex.Remove(seq.SequenceId);
            }

            _isRunning[seq.SequenceId] = false;
        }

        private void CleanupSequence(ActiveSequence seq)
        {
            if (seq.DelayCoroutine != null)
            {
                StopCoroutine(seq.DelayCoroutine);
                seq.DelayCoroutine = null;
            }

            if (_sequenceToWaitingEvents.TryGetValue(seq, out var waitingEvents))
            {
                foreach (int eventId in waitingEvents)
                {
                    if (_eventToWaitingSequences.TryGetValue(eventId, out var list))
                    {
                        list.Remove(seq);
                        if (list.Count == 0)
                        {
                            _eventToWaitingSequences.Remove(eventId);
                            UnsubscribeFromEvent(eventId);
                        }
                    }
                }

                _sequenceToWaitingEvents.Remove(seq);
            }
        }

        private void SubscribeToEvent(int eventId, ActiveSequence seq)
        {
            if (!_eventToWaitingSequences.TryGetValue(eventId, out var list))
            {
                list = new List<ActiveSequence>(4);
                _eventToWaitingSequences[eventId] = list;

                Action<EventData> callback = (eventData) => OnEventPublished(eventId, eventData);
                _eventSubscriptions[eventId] = callback;
            }

            list.Add(seq);
        }

        private void OnEventPublished(int eventId, EventData eventData)
        {
            if (_eventToWaitingSequences.TryGetValue(eventId, out var waitingSequences))
            {
                int count = waitingSequences.Count;
                int i = 0;

                while (i < count)
                {
                    var seq = waitingSequences[i];
                    if (!seq.IsWaiting || !_sequenceToWaitingEvents.TryGetValue(seq, out var events) ||
                        !events.Contains(eventId))
                    {
                        i++;
                        continue;
                    }

                    var waitStep = _sequences[seq.SequenceId].WaitSteps[seq.CurrentStep];
                    if (!waitStep.Matches(eventData))
                    {
                        i++;
                        continue;
                    }

                    RemoveSequenceFromWaiting(seq, eventId);
                    seq.IsWaiting = false;

                    var delay = _sequences[seq.SequenceId].Delays[seq.CurrentStep];
                    if (delay > 0)
                        ExecuteDelay(_sequences[seq.SequenceId], seq);
                    else
                        AdvanceToNextStep(seq);

                    waitingSequences[i] = waitingSequences[count - 1];
                    waitingSequences.RemoveAt(count - 1);
                    count--;
                }

                if (waitingSequences.Count == 0)
                {
                    _eventToWaitingSequences.Remove(eventId);
                    UnsubscribeFromEvent(eventId);
                }
            }
        }

        private void RemoveSequenceFromWaiting(ActiveSequence seq, int eventId)
        {
            if (_sequenceToWaitingEvents.TryGetValue(seq, out var events))
            {
                events.Remove(eventId);
                if (events.Count == 0)
                    _sequenceToWaitingEvents.Remove(seq);
            }
        }

        private void UnsubscribeFromEvent(int eventId)
        {
            _eventSubscriptions.Remove(eventId);
        }

        private void UnsubscribeFromAllEvents()
        {
            _eventSubscriptions.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ActiveSequence GetOrCreateActiveSequence()
        {
            if (_pool.Count > 0)
            {
                var seq = _pool.Dequeue();
                seq.SequenceId = -1;
                seq.CurrentStep = 0;
                seq.IsWaiting = false;
                seq.DelayCoroutine = null;
                return seq;
            }

            return new ActiveSequence();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReturnToPool(ActiveSequence seq)
        {
            seq.SequenceId = -1;
            seq.CurrentStep = 0;
            seq.IsWaiting = false;
            if (seq.DelayCoroutine != null)
            {
                StopCoroutine(seq.DelayCoroutine);
                seq.DelayCoroutine = null;
            }

            _pool.Enqueue(seq);
        }

        private void EnsureCapacity(int requiredIndex)
        {
            if (requiredIndex < _sequences.Length)
                return;

            int newSize = Mathf.NextPowerOfTwo(requiredIndex + 1);
            Array.Resize(ref _sequences, newSize);
            Array.Resize(ref _isRunning, newSize);
        }
    }
}