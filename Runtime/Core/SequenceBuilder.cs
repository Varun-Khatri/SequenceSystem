using System;
using System.Collections.Generic;

namespace VK.SequenceSystem.Core
{
    public class SequenceBuilder
    {
        private List<SequenceActionType> _actions = new List<SequenceActionType>(16);
        private List<SequenceStep> _singleSteps = new List<SequenceStep>(16);
        private List<ParallelStep> _parallelSteps = new List<ParallelStep>(16);
        private List<float> _delays = new List<float>(16);
        private List<int> _waitForEvents = new List<int>(16);
        private List<int> _waitForEventParams = new List<int>(16); // <-- ADD THIS: Parameters for wait events

        // Track which steps need which arrays
        private bool _hasParallelSteps = false;
        private bool _hasNonSingleSteps = false;
        private int _stepCount = 0;

        public SequenceBuilder AddEvent(int eventId)
        {
            return AddEvent(eventId, parameter: -1, waitForEventId: -1, delayAfter: 0f);
        }

        public SequenceBuilder AddEvent(int eventId, int parameter)
        {
            return AddEvent(eventId, parameter: parameter, waitForEventId: -1, delayAfter: 0f);
        }

        public SequenceBuilder AddEvent(int eventId, int parameter, int waitForEventId, float delayAfter = 0f)
        {
            _actions.Add(SequenceActionType.SingleEvent);
            _singleSteps.Add(SequenceStep.Create(eventId, waitForEventId, delayAfter, parameter));
            _parallelSteps.Add(default);
            _delays.Add(delayAfter);
            _waitForEvents.Add(waitForEventId);
            _waitForEventParams.Add(-1); // Not a wait event
            _stepCount++;
            return this;
        }

        public SequenceBuilder AddParallelEvents(params int[] eventIds)
        {
            return AddParallelEvents(eventIds, parameters: null, waitForEventId: -1, delayAfter: 0f);
        }

        public SequenceBuilder AddParallelEvents(int[] eventIds, int[] parameters = null, int waitForEventId = -1,
            float delayAfter = 0f)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _singleSteps.Add(default);

            // Create ParallelStep with parameters if provided
            var parallelStep = new ParallelStep
            {
                EventIds = eventIds,
                Parameters = parameters ?? Array.Empty<int>()
            };
            _parallelSteps.Add(parallelStep);

            _delays.Add(delayAfter);
            _waitForEvents.Add(waitForEventId);
            _waitForEventParams.Add(-1); // Not a wait event
            _hasParallelSteps = true;
            _hasNonSingleSteps = true;
            _stepCount++;
            return this;
        }

        public SequenceBuilder AddDelay(float seconds)
        {
            return AddDelay(seconds, waitForEventId: -1);
        }

        public SequenceBuilder AddDelay(float seconds, int waitForEventId)
        {
            _actions.Add(SequenceActionType.Delay);
            _singleSteps.Add(default);
            _parallelSteps.Add(default);
            _delays.Add(seconds);
            _waitForEvents.Add(waitForEventId);
            _waitForEventParams.Add(-1); // Not a wait event
            _hasNonSingleSteps = true;
            _stepCount++;
            return this;
        }

        public SequenceBuilder WaitForEvent(int eventId)
        {
            return WaitForEvent(eventId, expectedParameter: -1, delayAfter: 0f);
        }

        public SequenceBuilder WaitForEvent(int eventId, int expectedParameter)
        {
            return WaitForEvent(eventId, expectedParameter: expectedParameter, delayAfter: 0f);
        }

        public SequenceBuilder WaitForEvent(int eventId, int expectedParameter, float delayAfter)
        {
            _actions.Add(SequenceActionType.WaitForEvent);
            _singleSteps.Add(default);
            _parallelSteps.Add(default);
            _delays.Add(delayAfter);
            _waitForEvents.Add(eventId);
            _waitForEventParams.Add(expectedParameter); // <-- Store the expected parameter
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
                    Delays = Array.Empty<float>(),
                    WaitForEvents = Array.Empty<int>(),
                    WaitForEventParams = Array.Empty<int>() // <-- ADD THIS
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

            var data = new SequenceData
            {
                SequenceId = sequenceId,
                ActionTypes = _actions.ToArray(),
                SingleSteps = _singleSteps.ToArray(),
                ParallelSteps = parallelSteps,
                Delays = _delays.ToArray(),
                WaitForEvents = _waitForEvents.ToArray(),
                WaitForEventParams = _waitForEventParams.ToArray() // <-- ADD THIS
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
            _delays.Clear();
            _waitForEvents.Clear();
            _waitForEventParams.Clear(); // <-- ADD THIS
            _hasParallelSteps = false;
            _hasNonSingleSteps = false;
            _stepCount = 0;
        }
    }
}