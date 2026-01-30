using System;
using System.Collections.Generic;
using UnityEngine;

//using VK.Events;

namespace VK.SequenceSystem.Core
{
    public sealed class SequenceManager : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour _eventServiceSource;
        // private IEventService _events;

        private SequenceData[] _sequences;
        private bool[] _isRunning;

        private readonly List<ActiveSequence> _active = new();
        private readonly Queue<ActiveSequence> _pool = new();

        private readonly Dictionary<int, List<ActiveSequence>> _waiting = new();
        private readonly Dictionary<int, Action<IEventData>> _subscriptions = new();

        private sealed class ActiveSequence
        {
            public int Id;
            public int Step;
            public bool Waiting;
            public Coroutine Delay;
        }

        void Awake()
        {
            //_events = (IEventService)_eventServiceSource;
            _sequences = new SequenceData[128];
            _isRunning = new bool[128];
        }

        public void Register(int id, SequenceData data)
        {
            EnsureCapacity(id);
            data.Validate();
            _sequences[id] = data;
        }

        public void StartSequence(int id)
        {
            if (id >= _sequences.Length || !_sequences[id].IsValid || _isRunning[id])
                return;

            var seq = Get();
            seq.Id = id;
            seq.Step = 0;
            seq.Waiting = false;

            _active.Add(seq);
            _isRunning[id] = true;
            Execute(seq);
        }

        private void Execute(ActiveSequence seq)
        {
            var data = _sequences[seq.Id];
            if (seq.Step >= data.ActionTypes.Length)
            {
                Complete(seq);
                return;
            }

            switch (data.ActionTypes[seq.Step])
            {
                case SequenceActionType.SingleEvent:
                    Publish(data.SingleSteps[seq.Step].EventData);
                    Advance(seq);
                    break;

                case SequenceActionType.ParallelEvents:
                    foreach (var e in data.ParallelSteps[seq.Step].Events)
                        Publish(e);
                    Advance(seq);
                    break;

                case SequenceActionType.WaitForEvent:
                    Wait(seq, data.WaitSteps[seq.Step]);
                    break;

                case SequenceActionType.Delay:
                    seq.Delay = StartCoroutine(Delay(seq, data.Delays[seq.Step]));
                    break;
            }
        }

        private void Publish(IEventData data)
        {
            if (data == null || data.EventId <= 0) return;
            // _events.Publish(data.EventId, data);
        }

        private void Wait(ActiveSequence seq, WaitStep step)
        {
            seq.Waiting = true;

            if (!_waiting.TryGetValue(step.WaitEventId, out var list))
            {
                list = new List<ActiveSequence>();
                _waiting[step.WaitEventId] = list;

                Action<IEventData> cb = e => OnEvent(step.WaitEventId, e);
                _subscriptions[step.WaitEventId] = cb;
                //  _events.Subscribe(step.WaitEventId, cb);
            }

            list.Add(seq);
        }

        private void OnEvent(int id, IEventData data)
        {
            if (!_waiting.TryGetValue(id, out var list))
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var seq = list[i];
                var wait = _sequences[seq.Id].WaitSteps[seq.Step];

                if (!wait.Matches(data))
                    continue;

                list.RemoveAt(i);
                seq.Waiting = false;
                Advance(seq);
            }

            if (list.Count == 0)
            {
                _waiting.Remove(id);
                // _events.Unsubscribe(id, _subscriptions[id]);
                _subscriptions.Remove(id);
            }
        }

        private System.Collections.IEnumerator Delay(ActiveSequence seq, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Advance(seq);
        }

        private void Advance(ActiveSequence seq)
        {
            seq.Step++;
            Execute(seq);
        }

        private void Complete(ActiveSequence seq)
        {
            _isRunning[seq.Id] = false;
            _active.Remove(seq);
            Return(seq);
        }

        private ActiveSequence Get()
            => _pool.Count > 0 ? _pool.Dequeue() : new ActiveSequence();

        private void Return(ActiveSequence seq)
        {
            if (seq.Delay != null)
                StopCoroutine(seq.Delay);

            seq.Delay = null;
            _pool.Enqueue(seq);
        }

        private void EnsureCapacity(int id)
        {
            if (id < _sequences.Length) return;
            int size = Mathf.NextPowerOfTwo(id + 1);
            Array.Resize(ref _sequences, size);
            Array.Resize(ref _isRunning, size);
        }
    }
}