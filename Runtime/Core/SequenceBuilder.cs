using System.Collections.Generic;

namespace VK.SequenceSystem.Core
{
    public class SequenceBuilder
    {
        private readonly List<SequenceActionType> _actions = new(8);
        private readonly List<SequenceStep> _single = new(8);
        private readonly List<ParallelStep> _parallel = new(8);
        private readonly List<WaitStep> _wait = new(8);
        private readonly List<float> _delays = new(8);

        public SequenceBuilder ThenEvent(int eventId, int waitForId = -1, float delay = 0f)
        {
            _actions.Add(SequenceActionType.SingleEvent);
            _single.Add(SequenceStep.Create(eventId, waitForId, delay));
            _parallel.Add(default);
            _wait.Add(waitForId >= 0 ? WaitStep.EventOnly(waitForId) : default);
            _delays.Add(delay);
            return this;
        }

        public SequenceBuilder ThenEvent<T>(int eventId, T data = default,
            int waitForId = -1, float delay = 0f)
        {
            _actions.Add(SequenceActionType.SingleEvent);
            _single.Add(SequenceStep.Create(eventId, data, waitForId, delay));
            _parallel.Add(default);
            _wait.Add(waitForId >= 0 ? WaitStep.EventOnly(waitForId) : default);
            _delays.Add(delay);
            return this;
        }

        public SequenceBuilder ThenParallel(params IEventData[] events)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _single.Add(default);
            _parallel.Add(ParallelStep.Create(events));
            _wait.Add(default);
            _delays.Add(0f);
            return this;
        }

        public SequenceBuilder ThenWait<T>(int eventId, T expected = default)
        {
            _actions.Add(SequenceActionType.WaitForEvent);
            _single.Add(default);
            _parallel.Add(default);
            _wait.Add(WaitStep.Typed(eventId, expected));
            _delays.Add(0f);
            return this;
        }

        public SequenceBuilder ThenDelay(float seconds)
        {
            _actions.Add(SequenceActionType.Delay);
            _single.Add(default);
            _parallel.Add(default);
            _wait.Add(default);
            _delays.Add(seconds);
            return this;
        }

        /// <summary>
        /// Finalises the sequence and resets the builder for reuse.
        /// Calling Build() twice on the same builder without Reset() was previously unsafe.
        /// </summary>
        public SequenceData Build(int sequenceId)
        {
            var data = new SequenceData
            {
                SequenceId = sequenceId,
                ActionTypes = _actions.ToArray(),
                SingleSteps = _single.ToArray(),
                ParallelSteps = _parallel.ToArray(),
                WaitSteps = _wait.ToArray(),
                Delays = _delays.ToArray()
            };
            data.Validate();
            Reset(); // safe to reuse builder immediately
            return data;
        }

        /// <summary>Clears all pending steps. Called automatically by Build().</summary>
        public void Reset()
        {
            _actions.Clear();
            _single.Clear();
            _parallel.Clear();
            _wait.Clear();
            _delays.Clear();
        }
    }
}