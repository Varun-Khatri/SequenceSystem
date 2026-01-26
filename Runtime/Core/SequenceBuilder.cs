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

        // Track which steps need which arrays
        private bool _hasParallelSteps = false;
        private bool _hasNonSingleSteps = false;
        private int _stepCount = 0;

        public SequenceBuilder AddEvent(int eventId)
        {
            return AddEvent(eventId, waitForEventId: -1, delayAfter: 0f);
        }

        public SequenceBuilder AddEvent(int eventId, int waitForEventId, float delayAfter = 0f)
        {
            _actions.Add(SequenceActionType.SingleEvent);
            _singleSteps.Add(SequenceStep.Create(eventId, waitForEventId, delayAfter));
            _parallelSteps.Add(default);
            _delays.Add(delayAfter);
            _waitForEvents.Add(waitForEventId);
            _stepCount++;
            return this;
        }

        public SequenceBuilder AddParallelEvents(params int[] eventIds)
        {
            return AddParallelEvents(eventIds, waitForEventId: -1, delayAfter: 0f);
        }

        public SequenceBuilder AddParallelEvents(int[] eventIds, int waitForEventId, float delayAfter = 0f)
        {
            _actions.Add(SequenceActionType.ParallelEvents);
            _singleSteps.Add(default);
            _parallelSteps.Add(ParallelStep.Create(eventIds));
            _delays.Add(delayAfter);
            _waitForEvents.Add(waitForEventId);
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
            _hasNonSingleSteps = true;
            _stepCount++;
            return this;
        }

        public SequenceBuilder WaitForEvent(int eventId)
        {
            return WaitForEvent(eventId, delayAfter: 0f);
        }

        public SequenceBuilder WaitForEvent(int eventId, float delayAfter)
        {
            _actions.Add(SequenceActionType.WaitForEvent);
            _singleSteps.Add(default);
            _parallelSteps.Add(default);
            _delays.Add(delayAfter);
            _waitForEvents.Add(eventId);
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
                    WaitForEvents = Array.Empty<int>()
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
                WaitForEvents = _waitForEvents.ToArray()
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
            _hasParallelSteps = false;
            _hasNonSingleSteps = false;
            _stepCount = 0;
        }
    }
}