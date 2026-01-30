using System;
using System.Collections.Generic;
using UnityEngine;

namespace VK.SequenceSystem.Core
{
    [Serializable]
    public class SequenceStepData
    {
        public SequenceActionType Type;

        public int EventId;
        public string EventData; // Serialized string
        public int WaitForEventId = -1;
        public string WaitEventData; // Serialized string
        public float DelaySeconds;

        public int[] ParallelEventIds;
        public string[] ParallelEventData;

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

        // ---------- Parsing ----------

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

        public static bool TryParseWait(string raw, int eventId, out WaitStep step)
        {
            if (string.IsNullOrEmpty(raw))
            {
                step = WaitStep.Create<object>(eventId);
                return true;
            }

            if (int.TryParse(raw, out var i))
            {
                step = WaitStep.Create(eventId, i);
                return true;
            }

            if (float.TryParse(raw, out var f))
            {
                step = WaitStep.Create(eventId, f);
                return true;
            }

            if (bool.TryParse(raw, out var b))
            {
                step = WaitStep.Create(eventId, b);
                return true;
            }

            step = WaitStep.Create(eventId, raw);
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

                data.ActionTypes[i] = s.Type;
                data.Delays[i] = s.DelaySeconds;

                switch (s.Type)
                {
                    case SequenceActionType.SingleEvent:
                    {
                        SequenceStepData.TryParse(s.EventData, out var ev, s.EventId);
                        data.SingleSteps[i] = new SequenceStep
                        {
                            EventData = ev,
                            WaitForId = s.WaitForEventId,
                            Delay = s.DelaySeconds
                        };
                        break;
                    }

                    case SequenceActionType.ParallelEvents:
                    {
                        var list = new List<IEventData>();
                        for (int j = 0; j < s.ParallelEventIds.Length; j++)
                        {
                            string raw = (s.ParallelEventData != null && j < s.ParallelEventData.Length)
                                ? s.ParallelEventData[j]
                                : null;

                            SequenceStepData.TryParse(raw, out var ev, s.ParallelEventIds[j]);
                            list.Add(ev);
                        }

                        data.ParallelSteps[i] = ParallelStep.Create(list.ToArray());
                        break;
                    }

                    case SequenceActionType.WaitForEvent:
                    {
                        SequenceStepData.TryParseWait(
                            s.WaitEventData,
                            s.WaitForEventId,
                            out var wait);

                        data.WaitSteps[i] = wait;
                        break;
                    }

                    case SequenceActionType.Delay:
                    {
                        if (s.WaitForEventId >= 0)
                            data.WaitSteps[i] = WaitStep.Create<object>(s.WaitForEventId);
                        break;
                    }
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