using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VK.SequenceSystem.Core
{
    // Keep your existing structs but fix the issues

    public struct SequenceStep
    {
        public int EventId;
        public int WaitForEventId;
        public float DelaySeconds;
        public int Parameter; // <-- ADD THIS: For passing additional data

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SequenceStep Create(int eventId, int waitForId = -1, float delay = 0f, int parameter = -1)
            => new SequenceStep
            {
                EventId = eventId,
                WaitForEventId = waitForId,
                DelaySeconds = delay,
                Parameter = parameter
            };
    }

    public struct SequenceData
    {
        public int SequenceId;
        public SequenceActionType[] ActionTypes;
        public SequenceStep[] SingleSteps;
        public ParallelStep[] ParallelSteps;
        public float[] Delays;
        public int[] WaitForEvents;
        public int[] WaitForEventParams; // <-- ADD THIS: Parameters for wait events

        public bool IsValid => ActionTypes != null;

        // OPTIMIZATION: Validate on creation
        public void Validate()
        {
            if (ActionTypes == null) return;

            int length = ActionTypes.Length;
            if (SingleSteps.Length != length || ParallelSteps.Length != length ||
                Delays.Length != length || WaitForEvents.Length != length ||
                WaitForEventParams.Length != length) // <-- ADD THIS
            {
                throw new ArgumentException("All sequence arrays must have same length");
            }
        }
    }

    public struct ParallelStep
    {
        public int[] EventIds;
        public int[] Parameters; // <-- ADD THIS: Parameters for each event

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParallelStep Create(params int[] eventIds)
            => new ParallelStep
            {
                EventIds = eventIds ?? Array.Empty<int>(),
                Parameters = Array.Empty<int>() // Default empty
            };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParallelStep Create(int[] eventIds, int[] parameters)
            => new ParallelStep
            {
                EventIds = eventIds ?? Array.Empty<int>(),
                Parameters = parameters ?? Array.Empty<int>()
            };
    }

    public enum SequenceActionType : byte
    {
        SingleEvent = 0,
        ParallelEvents = 1,
        WaitForEvent = 2,
        Delay = 3
    }
}