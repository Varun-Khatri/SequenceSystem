using System;
using System.Collections.Generic;

namespace VK.SequenceSystem.Core
{
    public class SequenceBuilder
    {
        private List<SequenceActionType> _actions = new List<SequenceActionType>(16);
        private List<SequenceStep> _singleSteps = new List<SequenceStep>(16);
        private List<ParallelStep> _parallelSteps = new List<ParallelStep>(16);
        private List<WaitStep> _waitSteps = new List<WaitStep>(16);
        private List<float> _delays = new List<float>(16);

        // Track which steps need which arrays
        private bool _hasParallelSteps = false;
        private bool _hasWaitSteps = false;
        private bool _hasNonSingleSteps = false;
        private int _stepCount = 0;

        public SequenceBuilder AddEvent(int eventId)
        {
            return AddEvent(eventId, null, waitForId: -1, delay: 0f);
        }

        public SequenceBuilder AddEvent(int eventId, object data)
        {
            return AddEvent(eventId, data, waitForId: -1, delay: 0f);
        }

        public SequenceBuilder AddEvent(int eventId, int waitForId, float delay = 0f)
        {
            return AddEvent(eventId, null, waitForId, delay);
        }

        public SequenceBuilder AddEvent(int eventId, object data, int waitForId, float delay = 0f)
        {
            _actions.Add(SequenceActionType.SingleEvent);
            _singleSteps.Add(SequenceStep.Create(eventId, data, waitForId, delay));
            _parallelSteps.Add(default);
            _waitSteps.Add(WaitStep.Create(waitForId));
            _delays.Add(delay);
            _stepCount++;
            return this;
        }

        public SequenceBuilder AddParallelEvents(params int[] eventIds)
        {
            return AddParallelEvents(eventIds, waitForId: -1, delay: 0f);
        }

        public SequenceBuilder AddParallelEvents(int[] eventIds, int waitForId, float delay = 0f)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _singleSteps.Add(default);
            _parallelSteps.Add(ParallelStep.Create(eventIds));
            _waitSteps.Add(WaitStep.Create(waitForId));
            _delays.Add(delay);
            _hasParallelSteps = true;
            _hasNonSingleSteps = true;
            _stepCount++;
            return this;
        }

        public SequenceBuilder AddParallelEvents(params EventData[] eventData)
        {
            return AddParallelEvents(eventData, waitForId: -1, delay: 0f);
        }

        public SequenceBuilder AddParallelEvents(EventData[] eventData, int waitForId, float delay = 0f)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _singleSteps.Add(default);
            _parallelSteps.Add(ParallelStep.Create(eventData));
            _waitSteps.Add(WaitStep.Create(waitForId));
            _delays.Add(delay);
            _hasParallelSteps = true;
            _hasNonSingleSteps = true;
            _stepCount++;
            return this;
        }

        public SequenceBuilder AddDelay(float seconds)
        {
            return AddDelay(seconds, waitForId: -1);
        }

        public SequenceBuilder AddDelay(float seconds, int waitForId)
        {
            _actions.Add(SequenceActionType.Delay);
            _singleSteps.Add(default);
            _parallelSteps.Add(default);
            _waitSteps.Add(WaitStep.Create(waitForId));
            _delays.Add(seconds);
            _hasNonSingleSteps = true;
            _stepCount++;
            return this;
        }

        public SequenceBuilder WaitForEvent(int eventId)
        {
            return WaitForEvent(eventId, delay: 0f);
        }

        public SequenceBuilder WaitForEvent(int eventId, float delay)
        {
            return WaitForEvent(eventId, null, delay);
        }

        public SequenceBuilder WaitForEvent(int eventId, object expectedData)
        {
            return WaitForEvent(eventId, expectedData, 0f);
        }

        public SequenceBuilder WaitForEvent(int eventId, object expectedData, float delay)
        {
            _actions.Add(SequenceActionType.WaitForEvent);
            _singleSteps.Add(default);
            _parallelSteps.Add(default);

            // Create filter if expected data is provided
            Func<EventData, bool> filter = null;
            if (expectedData != null)
            {
                filter = (eventData) => object.Equals(eventData.Data, expectedData);
            }

            _waitSteps.Add(WaitStep.Create(eventId, filter));
            _delays.Add(delay);
            _hasWaitSteps = true;
            _hasNonSingleSteps = true;
            _stepCount++;
            return this;
        }

        public int StepCount => _stepCount;

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

            // Optimize allocation based on actual usage
            ParallelStep[] parallelSteps;
            if (_hasParallelSteps)
            {
                parallelSteps = new ParallelStep[_stepCount];
                // Copy only parallel steps
                for (int i = 0; i < _stepCount; i++)
                {
                    if (_actions[i] == SequenceActionType.ParallelEvents)
                    {
                        parallelSteps[i] = _parallelSteps[i];
                    }
                }
            }
            else
            {
                parallelSteps = Array.Empty<ParallelStep>();
            }

            WaitStep[] waitSteps;
            if (_hasWaitSteps)
            {
                waitSteps = new WaitStep[_stepCount];
                // Copy wait steps
                for (int i = 0; i < _stepCount; i++)
                {
                    waitSteps[i] = _waitSteps[i];
                }
            }
            else
            {
                waitSteps = Array.Empty<WaitStep>();
            }

            var data = new SequenceData
            {
                SequenceId = sequenceId,
                ActionTypes = _actions.ToArray(),
                SingleSteps = _singleSteps.ToArray(),
                ParallelSteps = parallelSteps,
                WaitSteps = waitSteps,
                Delays = _delays.ToArray()
            };

            // Clear unused SingleSteps entries for non-single-event steps
            if (_hasNonSingleSteps)
            {
                for (int i = 0; i < _stepCount; i++)
                {
                    if (_actions[i] != SequenceActionType.SingleEvent)
                    {
                        data.SingleSteps[i] = default;
                    }
                }
            }

            data.Validate();
            return data;
        }

        public void Clear()
        {
            _actions.Clear();
            _singleSteps.Clear();
            _parallelSteps.Clear();
            _waitSteps.Clear();
            _delays.Clear();
            _hasParallelSteps = false;
            _hasWaitSteps = false;
            _hasNonSingleSteps = false;
            _stepCount = 0;
        }
    }
}