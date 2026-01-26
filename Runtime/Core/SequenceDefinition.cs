using System;
using System.Collections.Generic;
using UnityEngine;

namespace VK.SequenceSystem.Core
{
    [System.Serializable]
    public class SequenceStepData
    {
        public SequenceActionType Type;
        public int EventId;
        public int WaitForEventId = -1;
        public float DelaySeconds;
        public int[] ParallelEventIds;

        // Helper properties for validation
        public bool IsValid
        {
            get
            {
                if (DelaySeconds < 0) return false;
                if (Type == SequenceActionType.ParallelEvents &&
                    (ParallelEventIds == null || ParallelEventIds.Length == 0))
                    return false;
                return true;
            }
        }

        // Factory methods
        public static SequenceStepData CreateEvent(int eventId, int waitFor = -1, float delay = 0f) =>
            new SequenceStepData
            {
                Type = SequenceActionType.SingleEvent, EventId = eventId, WaitForEventId = waitFor, DelaySeconds = delay
            };

        public static SequenceStepData CreateParallel(int[] eventIds, int waitFor = -1, float delay = 0f) =>
            new SequenceStepData
            {
                Type = SequenceActionType.ParallelEvents, ParallelEventIds = eventIds, WaitForEventId = waitFor,
                DelaySeconds = delay
            };

        public static SequenceStepData CreateWait(int eventId, float delay = 0f) =>
            new SequenceStepData
                { Type = SequenceActionType.WaitForEvent, WaitForEventId = eventId, DelaySeconds = delay };

        public static SequenceStepData CreateDelay(float seconds, int waitFor = -1) =>
            new SequenceStepData { Type = SequenceActionType.Delay, DelaySeconds = seconds, WaitForEventId = waitFor };
    }

    [CreateAssetMenu(menuName = "Systems/Sequence Definition")]
    public class SequenceDefinition : ScriptableObject
    {
        public int SequenceId;

        [SerializeField] private List<SequenceStepData> _steps = new List<SequenceStepData>();

        // Cache the runtime data to avoid re-converting
        private SequenceData? _cachedRuntimeData;

        public IReadOnlyList<SequenceStepData> Steps => _steps;

        public SequenceData GetRuntimeData()
        {
            if (_cachedRuntimeData.HasValue)
                return _cachedRuntimeData.Value;

            _cachedRuntimeData = ConvertToRuntimeData();
            return _cachedRuntimeData.Value;
        }

        private SequenceData ConvertToRuntimeData()
        {
            int stepCount = _steps.Count;

            if (stepCount == 0)
            {
                return new SequenceData
                {
                    SequenceId = SequenceId,
                    ActionTypes = Array.Empty<SequenceActionType>(),
                    SingleSteps = Array.Empty<SequenceStep>(),
                    ParallelSteps = Array.Empty<ParallelStep>(),
                    Delays = Array.Empty<float>(),
                    WaitForEvents = Array.Empty<int>()
                };
            }

            // Count parallel steps for optimized allocation
            int parallelStepCount = 0;
            for (int i = 0; i < stepCount; i++)
            {
                if (_steps[i].Type == SequenceActionType.ParallelEvents)
                    parallelStepCount++;
            }

            var data = new SequenceData
            {
                SequenceId = SequenceId,
                ActionTypes = new SequenceActionType[stepCount],
                SingleSteps = new SequenceStep[stepCount],
                // Only allocate ParallelSteps if needed
                ParallelSteps = parallelStepCount > 0 ? new ParallelStep[stepCount] : Array.Empty<ParallelStep>(),
                Delays = new float[stepCount],
                WaitForEvents = new int[stepCount]
            };

            for (int i = 0; i < stepCount; i++)
            {
                var step = _steps[i];

                // Apply validation fixes
                if (!step.IsValid)
                {
                    Debug.LogWarning($"Invalid step {i} in sequence {SequenceId}, skipping");
                    data.ActionTypes[i] = SequenceActionType.Delay; // Default to safe step
                    data.Delays[i] = 0f;
                    data.WaitForEvents[i] = -1;
                    continue;
                }

                data.ActionTypes[i] = step.Type;
                data.Delays[i] = step.DelaySeconds;
                data.WaitForEvents[i] = step.WaitForEventId;

                switch (step.Type)
                {
                    case SequenceActionType.SingleEvent:
                        data.SingleSteps[i] = SequenceStep.Create(
                            step.EventId,
                            step.WaitForEventId,
                            step.DelaySeconds
                        );
                        break;

                    case SequenceActionType.ParallelEvents:
                        if (data.ParallelSteps.Length > 0)
                        {
                            data.ParallelSteps[i] = ParallelStep.Create(step.ParallelEventIds);
                        }

                        // SingleSteps[i] remains default - intentionally unused
                        break;

                    case SequenceActionType.WaitForEvent:
                    case SequenceActionType.Delay:
                        // SingleSteps[i] remains default - intentionally unused
                        break;
                }
            }

            data.Validate();
            return data;
        }

        // Editor methods
#if UNITY_EDITOR
        public void AddStep(SequenceStepData step)
        {
            _steps.Add(step);
            _cachedRuntimeData = null;
        }

        public void RemoveStep(int index)
        {
            if (index >= 0 && index < _steps.Count)
            {
                _steps.RemoveAt(index);
                _cachedRuntimeData = null;
            }
        }

        public void MoveStep(int fromIndex, int toIndex)
        {
            if (fromIndex >= 0 && fromIndex < _steps.Count &&
                toIndex >= 0 && toIndex < _steps.Count)
            {
                var step = _steps[fromIndex];
                _steps.RemoveAt(fromIndex);
                _steps.Insert(toIndex, step);
                _cachedRuntimeData = null;
            }
        }

        private void OnValidate()
        {
            _cachedRuntimeData = null;

            // Validate all steps in editor
            for (int i = 0; i < _steps.Count; i++)
            {
                var step = _steps[i];
                if (step.DelaySeconds < 0)
                    step.DelaySeconds = 0;

                if (step.Type == SequenceActionType.ParallelEvents)
                {
                    if (step.ParallelEventIds == null)
                        step.ParallelEventIds = Array.Empty<int>();
                    else if (step.ParallelEventIds.Length == 0)
                        Debug.LogWarning($"ParallelEvents step {i} in sequence {SequenceId} has no events");
                }

                if (step.Type == SequenceActionType.SingleEvent && step.EventId == -1)
                    Debug.LogWarning($"SingleEvent step {i} in sequence {SequenceId} has EventId -1 (no event)");
            }
        }
#endif
    }
}