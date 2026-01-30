using System;
using System.Collections.Generic;
using UnityEngine;
using VK.SequenceSystem.Core;

namespace VK.SequenceSystem.Core
{
    [Serializable]
    public class SequenceStepData
    {
        public SequenceActionType ActionType;

        public int EventId;
        public string EventData; // Serialized string

        public int WaitForEventId = -1;
        public string WaitEventData; // Serialized string for typed wait
        public string WaitForTypeName; // Optional type name for typed wait

        public float DelaySeconds;

        public int[] ParallelEventIds;
        public string[] ParallelEventData;

        public bool IsValid
        {
            get
            {
                if (DelaySeconds < 0) return false;
                if (ActionType == SequenceActionType.ParallelEvents &&
                    (ParallelEventIds == null || ParallelEventIds.Length == 0))
                    return false;
                return true;
            }
        }

        // ---------------- Parsing ----------------

        public static bool TryParse(string raw, out IEventData data, int eventId)
        {
            data = null;
            if (string.IsNullOrEmpty(raw))
            {
                data = new EventData<object>(eventId, null);
                return true;
            }

            if (int.TryParse(raw, out var i))
            {
                data = new EventData<int>(eventId, i);
                return true;
            }

            if (float.TryParse(raw, out var f))
            {
                data = new EventData<float>(eventId, f);
                return true;
            }

            if (bool.TryParse(raw, out var b))
            {
                data = new EventData<bool>(eventId, b);
                return true;
            }

            data = new EventData<string>(eventId, raw);
            return true;
        }

        public static bool TryParseWait(string raw, int eventId, string typeName, out WaitStep step)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                // Event-only wait
                step = WaitStep.EventOnly(eventId);
                return true;
            }

            // Use only the parameter, not any instance field
            Type t = Type.GetType(typeName) ?? typeof(object);
            object value = raw;

            if (t == typeof(int) && int.TryParse(raw, out var i)) value = i;
            else if (t == typeof(float) && float.TryParse(raw, out var f)) value = f;
            else if (t == typeof(bool) && bool.TryParse(raw, out var b)) value = b;

            step = new WaitStep
            {
                WaitEventId = eventId,
                Mode = WaitMode.Typed,
                ExpectedType = t,
                ExpectedValue = value,
                HasValueFilter = true
            };

            return true;
        }
    }

    [CreateAssetMenu(menuName = "Systems/Sequence Definition")]
    public class SequenceDefinition : ScriptableObject
    {
        public int SequenceId;

        [SerializeField] private List<SequenceStepData> _steps = new();

        private SequenceData? _cachedRuntime;

        public SequenceData GetRuntimeData()
        {
            if (_cachedRuntime.HasValue)
                return _cachedRuntime.Value;

            _cachedRuntime = BuildRuntime();
            return _cachedRuntime.Value;
        }

        private SequenceData BuildRuntime()
        {
            int count = _steps.Count;

            var data = new SequenceData
            {
                SequenceId = SequenceId,
                ActionTypes = new SequenceActionType[count],
                SingleSteps = new SequenceStep[count],
                ParallelSteps = new ParallelStep[count],
                WaitSteps = new WaitStep[count],
                Delays = new float[count]
            };

            for (int i = 0; i < count; i++)
            {
                var s = _steps[i];

                if (!s.IsValid)
                {
                    data.ActionTypes[i] = SequenceActionType.Delay;
                    data.Delays[i] = 0;
                    continue;
                }

                data.ActionTypes[i] = s.ActionType;
                data.Delays[i] = s.DelaySeconds;

                switch (s.ActionType)
                {
                    case SequenceActionType.SingleEvent:
                        SequenceStepData.TryParse(s.EventData, out var ev, s.EventId);
                        data.SingleSteps[i] = new SequenceStep
                        {
                            EventData = ev,
                            WaitForId = s.WaitForEventId,
                            Delay = s.DelaySeconds
                        };
                        break;

                    case SequenceActionType.ParallelEvents:
                        var list = new List<IEventData>();
                        for (int j = 0; j < s.ParallelEventIds.Length; j++)
                        {
                            string raw = (s.ParallelEventData != null && j < s.ParallelEventData.Length)
                                ? s.ParallelEventData[j]
                                : null;

                            SequenceStepData.TryParse(raw, out var ev2, s.ParallelEventIds[j]);
                            list.Add(ev2);
                        }

                        data.ParallelSteps[i] = ParallelStep.Create(list.ToArray());
                        break;

                    case SequenceActionType.WaitForEvent:
                        SequenceStepData.TryParseWait(
                            s.WaitEventData,
                            s.WaitForEventId,
                            s.WaitForTypeName,
                            out var wait
                        );

                        data.WaitSteps[i] = wait;
                        break;

                    case SequenceActionType.Delay:
                        if (s.WaitForEventId >= 0)
                        {
                            data.WaitSteps[i] = WaitStep.EventOnly(s.WaitForEventId);
                        }

                        break;
                }
            }

            data.Validate();
            return data;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _cachedRuntime = null;
        }
#endif
    }
}