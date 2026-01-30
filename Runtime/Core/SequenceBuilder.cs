using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;

namespace VK.SequenceSystem.Core
{
    /// <summary>
    /// Optimized, fluent, and reusable sequence builder.
    /// Supports single events, parallel events, delays, and waiting for events.
    /// Ensures every step has a valid WaitStep to prevent default 0 IDs.
    /// </summary>
    public class SequenceBuilder
    {
        private readonly List<SequenceActionType> _actions = new(16);
        private readonly List<SequenceStep> _singleSteps = new(16);
        private readonly List<ParallelStep> _parallelSteps = new(16);
        private readonly List<WaitStep> _waitSteps = new(16);
        private readonly List<float> _delays = new(16);

        private int _stepCount = 0;

        public SequenceBuilder ThenEvent<T>(int eventId, T data = default, int waitForId = -1, float delay = 0f)
        {
            _actions.Add(SequenceActionType.SingleEvent);
            _singleSteps.Add(SequenceStep.Create(eventId, data, waitForId, delay));
            _parallelSteps.Add(default); // placeholder
            _waitSteps.Add(WaitStep.Create(waitForId >= 0 ? waitForId : -1));
            _delays.Add(delay);
            _stepCount++;
            return this;
        }

        public SequenceBuilder ThenEvent(int eventId, int waitForId = -1, float delay = 0f)
            => ThenEvent<object>(eventId, null, waitForId, delay);

        public SequenceBuilder ThenParallel(params int[] eventIds)
            => ThenParallel(eventIds, -1, 0f);

        public SequenceBuilder ThenParallel(int[] eventIds, int waitForId = -1, float delay = 0f)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _singleSteps.Add(SequenceStep.Create(-1)); // safe no-op
            _parallelSteps.Add(ParallelStep.Create(eventIds));
            _waitSteps.Add(WaitStep.Create(waitForId >= 0 ? waitForId : -1));
            _delays.Add(delay);
            _stepCount++;
            return this;
        }

        public SequenceBuilder ThenParallel(EventData[] eventData, int waitForId = -1, float delay = 0f)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _singleSteps.Add(SequenceStep.Create(-1));
            _parallelSteps.Add(ParallelStep.Create(eventData));
            _waitSteps.Add(WaitStep.Create(waitForId >= 0 ? waitForId : -1));
            _delays.Add(delay);
            _stepCount++;
            return this;
        }

        public SequenceBuilder ThenWait<T>(int eventId, T expectedData = default, float delay = 0f)
        {
            _actions.Add(SequenceActionType.WaitForEvent);
            _singleSteps.Add(SequenceStep.Create(-1));
            _parallelSteps.Add(default);

            Func<EventData, bool> filter = null;
            if (expectedData != null)
                filter = e => object.Equals(e.Data, expectedData);

            _waitSteps.Add(WaitStep.Create(eventId, filter));
            _delays.Add(delay);
            _stepCount++;
            return this;
        }

        public SequenceBuilder ThenDelay(float seconds)
        {
            _actions.Add(SequenceActionType.Delay);
            _singleSteps.Add(SequenceStep.Create(-1));
            _parallelSteps.Add(default);
            _waitSteps.Add(WaitStep.Create(-1));
            _delays.Add(seconds);
            _stepCount++;
            return this;
        }

        public SequenceData Build(int sequenceId)
        {
            if (_stepCount == 0)
                return new SequenceData
                {
                    SequenceId = sequenceId,
                    ActionTypes = Array.Empty<SequenceActionType>(),
                    SingleSteps = Array.Empty<SequenceStep>(),
                    ParallelSteps = Array.Empty<ParallelStep>(),
                    WaitSteps = Array.Empty<WaitStep>(),
                    Delays = Array.Empty<float>()
                };

            var actionsArray = ArrayPool<SequenceActionType>.Shared.Rent(_stepCount);
            var singleArray = ArrayPool<SequenceStep>.Shared.Rent(_stepCount);
            var parallelArray = ArrayPool<ParallelStep>.Shared.Rent(_stepCount);
            var waitArray = ArrayPool<WaitStep>.Shared.Rent(_stepCount);
            var delaysArray = ArrayPool<float>.Shared.Rent(_stepCount);

            _actions.CopyTo(actionsArray, 0);
            _singleSteps.CopyTo(singleArray, 0);
            _parallelSteps.CopyTo(parallelArray, 0);
            _delays.CopyTo(delaysArray, 0);

            // Ensure every step has valid WaitStep (prevents EventId=0 execution)
            for (int i = 0; i < _stepCount; i++)
                waitArray[i] = i < _waitSteps.Count ? _waitSteps[i] : WaitStep.Create(-1);

            return new SequenceData
            {
                SequenceId = sequenceId,
                ActionTypes = actionsArray,
                SingleSteps = singleArray,
                ParallelSteps = parallelArray,
                WaitSteps = waitArray,
                Delays = delaysArray
            };
        }

        public void Clear()
        {
            _actions.Clear();
            _singleSteps.Clear();
            _parallelSteps.Clear();
            _waitSteps.Clear();
            _delays.Clear();
            _stepCount = 0;
        }
    }
}