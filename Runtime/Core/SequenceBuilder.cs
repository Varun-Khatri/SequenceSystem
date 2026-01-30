using System;
using System.Buffers;
using System.Collections.Generic;

namespace VK.SequenceSystem.Core
{
    /// <summary>
    /// Optimized, fluent, and reusable sequence builder.
    /// Supports single events, parallel events, delays, and waiting for events.
    /// Uses generics for type-safe event payloads and minimizes allocations.
    /// </summary>
    public class SequenceBuilder
    {
        private readonly List<SequenceActionType> _actions = new(16);

        // Only store relevant steps; skip defaults to reduce allocations
        private readonly List<SequenceStep> _singleSteps = new(16);
        private readonly List<ParallelStep> _parallelSteps = new(16);
        private readonly List<WaitStep> _waitSteps = new(16);
        private readonly List<float> _delays = new(16);

        private int _stepCount = 0;

        // ========================
        // Fluent API Methods
        // ========================

        /// <summary>
        /// Add a single event with optional data.
        /// </summary>
        public SequenceBuilder ThenEvent<T>(int eventId, T data = default, int waitForId = -1, float delay = 0f)
        {
            _actions.Add(SequenceActionType.SingleEvent);
            _singleSteps.Add(SequenceStep.Create(eventId, data, waitForId, delay));
            _parallelSteps.Add(default);
            _waitSteps.Add(waitForId >= 0 ? WaitStep.Create(waitForId) : WaitStep.Create(-1));
            _delays.Add(delay);
            _stepCount++;
            return this;
        }

        public SequenceBuilder ThenEvent(int eventId, int waitForId = -1, float delay = 0f)
        {
            return ThenEvent<object>(eventId, null, waitForId, delay);
        }

        /// <summary>
        /// Add multiple events to run in parallel (no payload).
        /// </summary>
        public SequenceBuilder ThenParallel(params int[] eventIds)
        {
            return ThenParallel(eventIds, waitForId: -1, delay: 0f);
        }

        /// <summary>
        /// Add multiple events to run in parallel (generic EventData).
        /// </summary>
        public SequenceBuilder ThenParallel(params EventData[] eventData)
        {
            return ThenParallel(eventData, waitForId: -1, delay: 0f);
        }

        /// <summary>
        /// Add multiple parallel events with wait and delay.
        /// </summary>
        public SequenceBuilder ThenParallel(int[] eventIds, int waitForId = -1, float delay = 0f)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _singleSteps.Add(default);
            _parallelSteps.Add(ParallelStep.Create(eventIds));
            _waitSteps.Add(WaitStep.Create(waitForId));
            _delays.Add(delay);
            _stepCount++;
            return this;
        }

        /// <summary>
        /// Add multiple parallel events with EventData array.
        /// </summary>
        public SequenceBuilder ThenParallel(EventData[] eventData, int waitForId = -1, float delay = 0f)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _singleSteps.Add(default);
            _parallelSteps.Add(ParallelStep.Create(eventData));
            _waitSteps.Add(WaitStep.Create(waitForId));
            _delays.Add(delay);
            _stepCount++;
            return this;
        }

        /// <summary>
        /// Add a wait-for-event step with optional payload filter.
        /// </summary>
        public SequenceBuilder ThenWait<T>(int eventId, T expectedData = default, float delay = 0f)
        {
            _actions.Add(SequenceActionType.WaitForEvent);
            _singleSteps.Add(default);
            _parallelSteps.Add(default);

            Func<EventData, bool> filter = null;
            if (expectedData != null)
            {
                filter = (eventData) => object.Equals(eventData.Data, expectedData);
            }

            _waitSteps.Add(WaitStep.Create(eventId, filter));
            _delays.Add(delay);
            _stepCount++;
            return this;
        }

        /// <summary>
        /// Add a delay step.
        /// </summary>
        public SequenceBuilder ThenDelay(float seconds)
        {
            _actions.Add(SequenceActionType.Delay);
            _singleSteps.Add(default);
            _parallelSteps.Add(default);
            _waitSteps.Add(WaitStep.Create(-1));
            _delays.Add(seconds);
            _stepCount++;
            return this;
        }

        // ========================
        // Build & Utility Methods
        // ========================

        public int StepCount => _stepCount;

        /// <summary>
        /// Builds a SequenceData object.
        /// Minimizes allocations by pooling arrays.
        /// </summary>
        public SequenceData Build(int sequenceId)
        {
            if (_stepCount == 0)
            {
                return new SequenceData
                {
                    SequenceId = sequenceId,
                    ActionTypes = Array.Empty<SequenceActionType>(),
                    SingleSteps = Array.Empty<SequenceStep>(),
                    ParallelSteps = Array.Empty<ParallelStep>(),
                    WaitSteps = Array.Empty<WaitStep>(),
                    Delays = Array.Empty<float>()
                };
            }

            // Rent arrays from ArrayPool to minimize allocations
            var actionsArray = ArrayPool<SequenceActionType>.Shared.Rent(_stepCount);
            var singleArray = ArrayPool<SequenceStep>.Shared.Rent(_stepCount);
            var parallelArray = ArrayPool<ParallelStep>.Shared.Rent(_stepCount);
            var waitArray = ArrayPool<WaitStep>.Shared.Rent(_stepCount);
            var delaysArray = ArrayPool<float>.Shared.Rent(_stepCount);

            _actions.CopyTo(actionsArray, 0);
            _singleSteps.CopyTo(singleArray, 0);
            _parallelSteps.CopyTo(parallelArray, 0);
            _waitSteps.CopyTo(waitArray, 0);
            _delays.CopyTo(delaysArray, 0);

            var data = new SequenceData
            {
                SequenceId = sequenceId,
                ActionTypes = actionsArray,
                SingleSteps = singleArray,
                ParallelSteps = parallelArray,
                WaitSteps = waitArray,
                Delays = delaysArray
            };

            data.Validate();
            return data;
        }

        /// <summary>
        /// Clear all builder data for reuse.
        /// </summary>
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