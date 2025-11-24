using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Rust.PreventBlueprintWipes.Patches.UserPersistance;

using UserPersistance = global::UserPersistance;
using Database = global::Facepunch.Sqlite.Database;

[HarmonyPatch]
public static class ChangeBlueprintsPath
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(UserPersistance), MethodType.Constructor, typeof(string))]
    public static IEnumerable<CodeInstruction> Constructor(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var matcher = new CodeMatcher(instructions, generator);

        // 1. MATCH: Find the filename construction block
        matcher.MatchStartForward(
            new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(UserPersistance), nameof(UserPersistance.blueprints))),
            new CodeMatch(OpCodes.Ldloc_1), // Base Path (CRITICAL)
            new CodeMatch(it => it.opcode == OpCodes.Ldc_I4_0 || it.opcode == OpCodes.Ldc_I4_1 || it.opcode == OpCodes.Ldc_I4_2 || it.opcode == OpCodes.Ldc_I4_3 || it.opcode == OpCodes.Ldc_I4_4 || it.opcode == OpCodes.Ldc_I4_5 || it.opcode == OpCodes.Ldc_I4_6 || it.opcode == OpCodes.Ldc_I4_7 || it.opcode == OpCodes.Ldc_I4_8 || it.opcode == OpCodes.Ldc_I4 || it.opcode == OpCodes.Ldc_I4_S),      // Version Number (any int constant)
            new CodeMatch(OpCodes.Stloc_2),
            new CodeMatch(it => it.opcode == OpCodes.Ldloca_S && it.operand is LocalBuilder { LocalIndex: 2 }),
            new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(int), nameof(int.ToString))),
            new CodeMatch(OpCodes.Ldstr, ".db"),
            new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(string), nameof(string.Concat), new[] { typeof(string), typeof(string), typeof(string) })),
            new CodeMatch(OpCodes.Ldc_I4_1), // The 'true' flag
            new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Database), nameof(Database.Open)))
        );

        if (matcher.IsInvalid)
        {
            Debug.LogError("[PreventBlueprintWipes] PATCH FAILED: IL Mismatch.");
            return instructions;
        }

        // 2. ADVANCE: Skip 'Ldsfld' (0) and 'Ldloc_1' (1).
        matcher.Advance(2);

        // 3. REMOVE: Delete the Version + Extension logic (6 instructions)
        matcher.RemoveInstructions(6);

        // 4. INJECT: Add our static filename and use 2-way Concat.
        matcher.InsertAndAdvance(
            new CodeInstruction(OpCodes.Ldstr, "player.blueprints.db"),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(string), nameof(string.Concat), new[] { typeof(string), typeof(string) }))
        );

        return matcher.Instructions();
    }
}
