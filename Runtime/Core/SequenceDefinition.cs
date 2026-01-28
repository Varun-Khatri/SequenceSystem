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
        public string EventData; // Serialized as string for ScriptableObject
        public int WaitForEventId = -1;
        public string WaitEventData; // Expected data for wait events
        public float DelaySeconds;
        public int[] ParallelEventIds;
        public string[] ParallelEventData; // Data for parallel events

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

        // Helper method to parse data
        public object GetParsedEventData()
        {
            return ParseData(EventData);
        }

        public object GetParsedWaitEventData()
        {
            return ParseData(WaitEventData);
        }

        private object ParseData(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;

            // Simple parsing logic - you might want to extend this
            if (int.TryParse(data, out int intValue)) return intValue;
            if (float.TryParse(data, out float floatValue)) return floatValue;
            if (bool.TryParse(data, out bool boolValue)) return boolValue;

            return data; // Return as string
        }

        // Factory methods
        public static SequenceStepData
            CreateEvent(int eventId, object data = null, int waitFor = -1, float delay = 0f) =>
            new SequenceStepData
            {
                Type = SequenceActionType.SingleEvent,
                EventId = eventId,
                EventData = data?.ToString(),
                WaitForEventId = waitFor,
                DelaySeconds = delay
            };

        public static SequenceStepData CreateParallel(int[] eventIds, string[] eventData = null, int waitFor = -1,
            float delay = 0f) =>
            new SequenceStepData
            {
                Type = SequenceActionType.ParallelEvents,
                ParallelEventIds = eventIds,
                ParallelEventData = eventData,
                WaitForEventId = waitFor,
                DelaySeconds = delay
            };

        public static SequenceStepData CreateWait(int eventId, object expectedData = null, float delay = 0f) =>
            new SequenceStepData
            {
                Type = SequenceActionType.WaitForEvent,
                WaitForEventId = eventId,
                WaitEventData = expectedData?.ToString(),
                DelaySeconds = delay
            };

        public static SequenceStepData CreateDelay(float seconds, int waitFor = -1) =>
            new SequenceStepData
            {
                Type = SequenceActionType.Delay,
                DelaySeconds = seconds,
                WaitForEventId = waitFor
            };
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
                    WaitSteps = Array.Empty<WaitStep>(),
                    Delays = Array.Empty<float>()
                };
            }

            // Count steps for optimized allocation
            int parallelStepCount = 0;
            int waitStepCount = 0;
            for (int i = 0; i < stepCount; i++)
            {
                if (_steps[i].Type == SequenceActionType.ParallelEvents)
                    parallelStepCount++;
                if (_steps[i].Type == SequenceActionType.WaitForEvent)
                    waitStepCount++;
            }

            var data = new SequenceData
            {
                SequenceId = SequenceId,
                ActionTypes = new SequenceActionType[stepCount],
                SingleSteps = new SequenceStep[stepCount],
                ParallelSteps = parallelStepCount > 0 ? new ParallelStep[stepCount] : Array.Empty<ParallelStep>(),
                WaitSteps = waitStepCount > 0 ? new WaitStep[stepCount] : Array.Empty<WaitStep>(),
                Delays = new float[stepCount]
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
                    continue;
                }

                data.ActionTypes[i] = step.Type;
                data.Delays[i] = step.DelaySeconds;

                switch (step.Type)
                {
                    case SequenceActionType.SingleEvent:
                        var eventData = step.GetParsedEventData();
                        data.SingleSteps[i] = SequenceStep.Create(
                            step.EventId,
                            eventData,
                            step.WaitForEventId,
                            step.DelaySeconds
                        );
                        break;

                    case SequenceActionType.ParallelEvents:
                        if (data.ParallelSteps.Length > 0)
                        {
                            if (step.ParallelEventIds != null && step.ParallelEventIds.Length > 0)
                            {
                                var eventDataArray = new EventData[step.ParallelEventIds.Length];
                                for (int j = 0; j < step.ParallelEventIds.Length; j++)
                                {
                                    object dataObj = null;
                                    if (step.ParallelEventData != null && j < step.ParallelEventData.Length)
                                    {
                                        // Parse parallel event data
                                        if (!string.IsNullOrEmpty(step.ParallelEventData[j]))
                                        {
                                            if (int.TryParse(step.ParallelEventData[j], out int intVal))
                                                dataObj = intVal;
                                            else if (float.TryParse(step.ParallelEventData[j], out float floatVal))
                                                dataObj = floatVal;
                                            else
                                                dataObj = step.ParallelEventData[j];
                                        }
                                    }

                                    eventDataArray[j] = new EventData(step.ParallelEventIds[j], dataObj);
                                }

                                data.ParallelSteps[i] = ParallelStep.Create(eventDataArray);
                            }
                        }

                        break;

                    case SequenceActionType.WaitForEvent:
                        if (data.WaitSteps.Length > 0)
                        {
                            var expectedData = step.GetParsedWaitEventData();
                            Func<EventData, bool> filter = null;
                            if (expectedData != null)
                            {
                                filter = (eventData) => object.Equals(eventData.Data, expectedData);
                            }

                            data.WaitSteps[i] = WaitStep.Create(step.WaitForEventId, filter);
                        }

                        break;

                    case SequenceActionType.Delay:
                        if (data.WaitSteps.Length > 0)
                        {
                            data.WaitSteps[i] = WaitStep.Create(step.WaitForEventId);
                        }

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

                    if (step.ParallelEventData == null)
                        step.ParallelEventData = Array.Empty<string>();
                }

                if (step.Type == SequenceActionType.SingleEvent && step.EventId == -1)
                    Debug.LogWarning($"SingleEvent step {i} in sequence {SequenceId} has EventId -1 (no event)");
            }
        }
#endif
    }
}