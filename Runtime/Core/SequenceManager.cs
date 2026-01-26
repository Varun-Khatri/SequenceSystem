using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VK.SequenceSystem.Core
{
    public class SequenceManager : MonoBehaviour
    {
        // Fast lookups using arrays indexed by sequence ID
        private SequenceData[] _sequences;
        private bool[] _isRunning;

        // OPTIMIZATION: HashSet for O(1) active sequence lookup
        private HashSet<ActiveSequence> _activeSequenceSet = new HashSet<ActiveSequence>();

        // For active sequences
        private class ActiveSequence
        {
            public int SequenceId;
            public int CurrentStep;
            public bool IsWaiting;
            public Coroutine DelayCoroutine;
        }

        private List<ActiveSequence> _activeSequences = new List<ActiveSequence>(16);
        private readonly Queue<ActiveSequence> _pool = new Queue<ActiveSequence>();

        // Bidirectional tracking for O(1) cleanup
        private Dictionary<int, List<ActiveSequence>> _eventToWaitingSequences =
            new Dictionary<int, List<ActiveSequence>>(32);

        private Dictionary<ActiveSequence, HashSet<int>> _sequenceToWaitingEvents =
            new Dictionary<ActiveSequence, HashSet<int>>(32);

        // OPTIMIZATION: Cache for frequently accessed data
        private Dictionary<int, int> _sequenceToIndex = new Dictionary<int, int>(32);

        void Awake()
        {
            // Initialize with capacity
            const int INITIAL_CAPACITY = 128;
            _sequences = new SequenceData[INITIAL_CAPACITY];
            _isRunning = new bool[INITIAL_CAPACITY];
        }

        void OnDestroy()
        {
            StopAllSequences();
        }

        // Public API
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterSequence(int sequenceId, SequenceData data)
        {
            EnsureCapacity(sequenceId);

            // OPTIMIZATION: Validate before storing
            if (data.IsValid)
            {
                data.Validate();
                _sequences[sequenceId] = data;
            }
            else
            {
                Debug.LogError($"Attempted to register invalid sequence data for ID: {sequenceId}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StartSequence(int sequenceId)
        {
            if (sequenceId >= _sequences.Length || !_sequences[sequenceId].IsValid)
                return;

            if (_isRunning[sequenceId])
                return;

            var sequence = GetOrCreateActiveSequence();
            sequence.SequenceId = sequenceId;
            sequence.CurrentStep = 0;
            sequence.IsWaiting = false;
            sequence.DelayCoroutine = null;

            _activeSequences.Add(sequence);
            _activeSequenceSet.Add(sequence);
            _sequenceToIndex[sequenceId] = _activeSequences.Count - 1;
            _isRunning[sequenceId] = true;

            ExecuteCurrentStep(sequence);
        }

        public void StopSequence(int sequenceId)
        {
            // OPTIMIZATION: Use cached index for O(1) lookup
            if (_sequenceToIndex.TryGetValue(sequenceId, out int index) &&
                index < _activeSequences.Count &&
                _activeSequences[index].SequenceId == sequenceId)
            {
                var seq = _activeSequences[index];
                CleanupSequence(seq);
                ReturnToPool(seq);
                _activeSequenceSet.Remove(seq);

                // Remove from active list with swap-remove
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
                // Fallback to linear search (rare case)
                for (int i = 0; i < _activeSequences.Count; i++)
                {
                    if (_activeSequences[i].SequenceId == sequenceId)
                    {
                        var seq = _activeSequences[i];
                        CleanupSequence(seq);
                        ReturnToPool(seq);
                        _activeSequenceSet.Remove(seq);

                        // Update indices
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
        }

        // Event system integration - called when any event is published
        public void OnEventPublished(int eventId)
        {
            // Check if any sequences are waiting for this event
            if (_eventToWaitingSequences.TryGetValue(eventId, out var waitingSequences))
            {
                int count = waitingSequences.Count;

                // OPTIMIZATION: Process with while loop to handle removals efficiently
                int i = 0;
                while (i < count)
                {
                    var seq = waitingSequences[i];

                    // Validate this sequence is still waiting for this event
                    if (!seq.IsWaiting || !IsSequenceWaitingForEvent(seq, eventId))
                    {
                        i++;
                        continue;
                    }

                    RemoveSequenceFromWaiting(seq, eventId);
                    seq.IsWaiting = false;

                    // Check for delay after waiting
                    var data = _sequences[seq.SequenceId];
                    var delay = data.Delays[seq.CurrentStep];

                    if (delay > 0)
                    {
                        ExecuteDelay(data, seq);
                    }
                    else
                    {
                        AdvanceToNextStep(seq);
                    }

                    // Swap-remove for O(1) removal
                    waitingSequences[i] = waitingSequences[count - 1];
                    waitingSequences.RemoveAt(count - 1);
                    count--;
                    // Don't increment i, check new element at position i
                }

                // Clean up empty lists
                if (waitingSequences.Count == 0)
                {
                    _eventToWaitingSequences.Remove(eventId);
                }
            }
        }

        // Core execution logic
        private void ExecuteCurrentStep(ActiveSequence seq)
        {
            var data = _sequences[seq.SequenceId];

            // Check if sequence is complete
            if (seq.CurrentStep >= data.ActionTypes.Length)
            {
                CompleteSequence(seq);
                return;
            }

            var actionType = data.ActionTypes[seq.CurrentStep];

            // OPTIMIZATION: Use if-else chain instead of switch for hot path
            if (actionType == SequenceActionType.SingleEvent)
            {
                ExecuteSingleEvent(data, seq);
            }
            else if (actionType == SequenceActionType.ParallelEvents)
            {
                ExecuteParallelEvents(data, seq);
            }
            else if (actionType == SequenceActionType.WaitForEvent)
            {
                ExecuteWaitForEvent(data, seq);
            }
            else // Delay
            {
                ExecuteDelay(data, seq);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteSingleEvent(SequenceData data, ActiveSequence seq)
        {
            int eventId = data.SingleSteps[seq.CurrentStep].EventId;
            if (eventId != -1)
            {
                // _eventSystem.Publish(eventId);
            }

            CheckPostExecution(data, seq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExecuteParallelEvents(SequenceData data, ActiveSequence seq)
        {
            var parallelStep = data.ParallelSteps[seq.CurrentStep];
            if (parallelStep.EventIds != null && parallelStep.EventIds.Length > 0)
            {
                // OPTIMIZATION: Local variable for array bounds check elimination
                var eventIds = parallelStep.EventIds;
                int length = eventIds.Length;

                for (int i = 0; i < length; i++)
                {
                    int eventId = eventIds[i];
                    if (eventId != -1)
                    {
                        // _eventSystem.Publish(eventId);
                    }
                }
            }

            CheckPostExecution(data, seq);
        }

        private void ExecuteWaitForEvent(SequenceData data, ActiveSequence seq)
        {
            int waitEventId = data.WaitForEvents[seq.CurrentStep];
            if (waitEventId != -1)
            {
                seq.IsWaiting = true;

                // Add to waiting list
                if (!_eventToWaitingSequences.TryGetValue(waitEventId, out var list))
                {
                    list = new List<ActiveSequence>(4);
                    _eventToWaitingSequences[waitEventId] = list;
                }

                list.Add(seq);

                // Track reverse mapping
                if (!_sequenceToWaitingEvents.TryGetValue(seq, out var events))
                {
                    events = new HashSet<int>();
                    _sequenceToWaitingEvents[seq] = events;
                }

                events.Add(waitEventId);
            }
            else
            {
                AdvanceToNextStep(seq);
            }
        }

        private void ExecuteDelay(SequenceData data, ActiveSequence seq)
        {
            float delay = data.Delays[seq.CurrentStep];

            // OPTIMIZATION: Validate delay before starting coroutine
            if (delay > 0 && delay <= 3600f) // Max 1 hour delay
            {
                seq.IsWaiting = true;
                seq.DelayCoroutine = StartCoroutine(DelayCoroutine(seq, delay));
            }
            else if (delay > 3600f)
            {
                Debug.LogWarning($"Delay too long ({delay}s) in sequence {seq.SequenceId}");
                AdvanceToNextStep(seq);
            }
            else
            {
                AdvanceToNextStep(seq);
            }
        }

        private System.Collections.IEnumerator DelayCoroutine(ActiveSequence seq, float delay)
        {
            yield return new WaitForSeconds(delay);

            // OPTIMIZATION: O(1) lookup instead of O(n) Contains
            if (_activeSequenceSet.Contains(seq) && seq.IsWaiting)
            {
                seq.IsWaiting = false;
                seq.DelayCoroutine = null;

                // Check for wait event after delay
                var data = _sequences[seq.SequenceId];
                var waitId = data.WaitForEvents[seq.CurrentStep];

                if (waitId != -1)
                {
                    ExecuteWaitForEvent(data, seq);
                }
                else
                {
                    AdvanceToNextStep(seq);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckPostExecution(SequenceData data, ActiveSequence seq)
        {
            int waitId = data.WaitForEvents[seq.CurrentStep];
            if (waitId != -1)
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

            // Stop any delay coroutine
            if (seq.DelayCoroutine != null)
            {
                StopCoroutine(seq.DelayCoroutine);
                seq.DelayCoroutine = null;
            }

            ExecuteCurrentStep(seq);
        }

        private void CompleteSequence(ActiveSequence seq)
        {
            // Publish sequence completed event
            // _eventSystem.Publish(EventIds.SEQUENCE_COMPLETED, seq.SequenceId);

            CleanupSequence(seq);
            ReturnToPool(seq);
            _activeSequenceSet.Remove(seq);

            // OPTIMIZATION: Update index mapping
            if (_sequenceToIndex.TryGetValue(seq.SequenceId, out int index))
            {
                // Remove with swap-remove
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
            // Stop any running coroutines
            if (seq.DelayCoroutine != null)
            {
                StopCoroutine(seq.DelayCoroutine);
                seq.DelayCoroutine = null;
            }

            // Efficient cleanup using reverse mapping
            if (_sequenceToWaitingEvents.TryGetValue(seq, out var waitingEvents))
            {
                foreach (int eventId in waitingEvents)
                {
                    if (_eventToWaitingSequences.TryGetValue(eventId, out var list))
                    {
                        // OPTIMIZATION: Linear search but small lists
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] == seq)
                            {
                                // Swap-remove
                                int lastIndex = list.Count - 1;
                                if (i != lastIndex)
                                {
                                    list[i] = list[lastIndex];
                                }

                                list.RemoveAt(lastIndex);

                                if (list.Count == 0)
                                {
                                    _eventToWaitingSequences.Remove(eventId);
                                }

                                break;
                            }
                        }
                    }
                }

                _sequenceToWaitingEvents.Remove(seq);
            }
        }

        // Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsSequenceWaitingForEvent(ActiveSequence seq, int eventId)
        {
            return _sequenceToWaitingEvents.TryGetValue(seq, out var events) &&
                   events.Contains(eventId);
        }

        private void RemoveSequenceFromWaiting(ActiveSequence seq, int eventId)
        {
            if (_sequenceToWaitingEvents.TryGetValue(seq, out var events))
            {
                events.Remove(eventId);
                if (events.Count == 0)
                {
                    _sequenceToWaitingEvents.Remove(seq);
                }
            }
        }

        // Pool management
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ActiveSequence GetOrCreateActiveSequence()
        {
            if (_pool.Count > 0)
                return _pool.Dequeue();
            return new ActiveSequence();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReturnToPool(ActiveSequence seq)
        {
            // OPTIMIZATION: Clear fields for pooled object
            seq.SequenceId = -1;
            seq.CurrentStep = 0;
            seq.IsWaiting = false;
            seq.DelayCoroutine = null;
            _pool.Enqueue(seq);
        }

        // Array capacity management
        private void EnsureCapacity(int requiredIndex)
        {
            if (requiredIndex < _sequences.Length)
                return;

            int newSize = Mathf.NextPowerOfTwo(requiredIndex + 1);
            Array.Resize(ref _sequences, newSize);
            Array.Resize(ref _isRunning, newSize);
        }

        // OPTIMIZATION: Debug/utility methods
#if UNITY_EDITOR
        public int GetActiveSequenceCount() => _activeSequences.Count;

        public (int id, int step, bool waiting)[] GetActiveSequenceInfo()
        {
            var info = new (int id, int step, bool waiting)[_activeSequences.Count];
            for (int i = 0; i < _activeSequences.Count; i++)
            {
                var seq = _activeSequences[i];
                info[i] = (seq.SequenceId, seq.CurrentStep, seq.IsWaiting);
            }

            return info;
        }
#endif
    }
}