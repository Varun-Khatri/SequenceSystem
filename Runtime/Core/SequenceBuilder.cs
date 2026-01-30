using System.Collections.Generic;

namespace VK.SequenceSystem.Core
{
    public class SequenceBuilder
    {
        private readonly List<SequenceActionType> _actions = new();
        private readonly List<SequenceStep> _single = new();
        private readonly List<ParallelStep> _parallel = new();
        private readonly List<WaitStep> _wait = new();
        private readonly List<float> _delays = new();

        public SequenceBuilder ThenEvent(
            int eventId,
            int waitForId = -1,
            float delay = 0f)
        {
            _actions.Add(SequenceActionType.SingleEvent);
            _single.Add(SequenceStep.Create(eventId, waitForId, delay));
            _parallel.Add(default);

            _wait.Add(waitForId >= 0
                ? WaitStep.EventOnly(waitForId)
                : default);

            _delays.Add(delay);
            return this;
        }

        public SequenceBuilder ThenEvent<T>(
            int eventId,
            T data = default,
            int waitForId = -1,
            float delay = 0f)
        {
            _actions.Add(SequenceActionType.SingleEvent);
            _single.Add(SequenceStep.Create(eventId, data, waitForId, delay));
            _parallel.Add(default);

            _wait.Add(waitForId >= 0
                ? WaitStep.EventOnly(waitForId)
                : default);

            _delays.Add(delay);
            return this;
        }

        public SequenceBuilder ThenParallel(params IEventData[] events)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _single.Add(default);
            _parallel.Add(ParallelStep.Create(events));
            _wait.Add(default);
            _delays.Add(0);
            return this;
        }

        public SequenceBuilder ThenWait<T>(int eventId, T expected = default)
        {
            _actions.Add(SequenceActionType.WaitForEvent);
            _single.Add(default);
            _parallel.Add(default);
            _wait.Add(WaitStep.Typed(eventId, expected));
            _delays.Add(0);
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
            return data;
        }
    }
}