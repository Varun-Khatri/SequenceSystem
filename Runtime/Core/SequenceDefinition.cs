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

            var data = new SequenceData
            {
                SequenceId = SequenceId,
                ActionTypes = new SequenceActionType[stepCount],
                SingleSteps = new SequenceStep[stepCount],
                ParallelSteps = new ParallelStep[stepCount],
                WaitSteps = new WaitStep[stepCount],
                Delays = new float[stepCount]
            };

            for (int i = 0; i < stepCount; i++)
            {
                var step = _steps[i];

                // Validate defensively
                if (!step.IsValid)
                {
                    Debug.LogWarning(
                        $"Invalid step {i} in sequence {SequenceId}, replacing with safe Delay(0)"
                    );

                    data.ActionTypes[i] = SequenceActionType.Delay;
                    data.Delays[i] = 0f;
                    continue;
                }

                data.ActionTypes[i] = step.Type;
                data.Delays[i] = step.DelaySeconds;

                switch (step.Type)
                {
                    case SequenceActionType.SingleEvent:
                    {
                        data.SingleSteps[i] = SequenceStep.Create(
                            step.EventId,
                            step.GetParsedEventData(),
                            step.WaitForEventId,
                            step.DelaySeconds
                        );
                        break;
                    }

                    case SequenceActionType.ParallelEvents:
                    {
                        var ids = step.ParallelEventIds;
                        if (ids != null && ids.Length > 0)
                        {
                            var eventDataArray = new EventData[ids.Length];

                            for (int j = 0; j < ids.Length; j++)
                            {
                                object dataObj = null;

                                if (step.ParallelEventData != null &&
                                    j < step.ParallelEventData.Length &&
                                    !string.IsNullOrEmpty(step.ParallelEventData[j]))
                                {
                                    var raw = step.ParallelEventData[j];

                                    if (int.TryParse(raw, out int intVal))
                                        dataObj = intVal;
                                    else if (float.TryParse(raw, out float floatVal))
                                        dataObj = floatVal;
                                    else if (bool.TryParse(raw, out bool boolVal))
                                        dataObj = boolVal;
                                    else
                                        dataObj = raw;
                                }

                                eventDataArray[j] = new EventData(ids[j], dataObj);
                            }

                            data.ParallelSteps[i] = ParallelStep.Create(eventDataArray);
                        }

                        break;
                    }

                    case SequenceActionType.WaitForEvent:
                    {
                        var expected = step.GetParsedWaitEventData();
                        data.WaitSteps[i] = WaitStep.Create(step.WaitForEventId, expected);
                        break;
                    }

                    case SequenceActionType.Delay:
                    {
                        if (step.WaitForEventId != -1)
                        {
                            data.WaitSteps[i] = WaitStep.Create(step.WaitForEventId);
                        }

                        break;
                    }
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