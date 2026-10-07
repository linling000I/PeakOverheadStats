using System;
using System.Reflection;
using UnityEngine;

namespace PeakOverheadStats;

/// <summary>
/// Reflection helpers for accessing private/internal game fields.
/// </summary>
internal static class GameRef
{
    // RunManager
    private static readonly FieldInfo _runManager_timeSinceRunStarted =
        typeof(RunManager).GetField("timeSinceRunStarted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

    public static float GetTimeSinceRunStarted(RunManager rm) =>
        _runManager_timeSinceRunStarted != null ? (float)_runManager_timeSinceRunStarted.GetValue(rm) : Time.time;

    // StaminaBar
    private static readonly FieldInfo _staminaBar_desiredStaminaSize =
        typeof(StaminaBar).GetField("desiredStaminaSize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo _staminaBar_desiredExtraStaminaSize =
        typeof(StaminaBar).GetField("desiredExtraStaminaSize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    public static float GetDesiredStaminaSize(StaminaBar bar) =>
        _staminaBar_desiredStaminaSize != null ? (float)_staminaBar_desiredStaminaSize.GetValue(bar) : 0f;
    public static float GetDesiredExtraStaminaSize(StaminaBar bar) =>
        _staminaBar_desiredExtraStaminaSize != null ? (float)_staminaBar_desiredExtraStaminaSize.GetValue(bar) : 0f;

    // OrbFogHandler
    private static readonly FieldInfo _orbFogHandler_sphere =
        typeof(OrbFogHandler).GetField("sphere", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    public static object GetFogSphere(OrbFogHandler handler) =>
        _orbFogHandler_sphere?.GetValue(handler);

    // CharacterStats
    private static readonly FieldInfo _characterStats_character =
        typeof(CharacterStats).GetField("character", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    public static Character GetCharacter(CharacterStats stats) =>
        _characterStats_character != null ? (Character)_characterStats_character.GetValue(stats) : null;

    // LavaRising
    private static readonly FieldInfo _lavaRising_startHeight =
        typeof(LavaRising).GetField("startHeight", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    public static float GetStartHeight(LavaRising lava) =>
        _lavaRising_startHeight != null ? (float)_lavaRising_startHeight.GetValue(lava) : 0f;

    // CharacterData
    private static readonly FieldInfo _characterData_isInvincible =
        typeof(CharacterData).GetField("isInvincible", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    public static bool GetIsInvincible(CharacterData data) =>
        _characterData_isInvincible != null && (bool)_characterData_isInvincible.GetValue(data);

    // ThornOnMe
    private static FieldInfo _thornOnMe_popOutTime;
    public static float GetPopOutTime(object thorn)
    {
        if (_thornOnMe_popOutTime == null && thorn != null)
            _thornOnMe_popOutTime = thorn.GetType().GetField("popOutTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return _thornOnMe_popOutTime != null ? (float)_thornOnMe_popOutTime.GetValue(thorn) : 0f;
    }

    // CharacterAfflictions
    private static FieldInfo _characterAfflictions_m_inAirport;
    public static bool GetInAirport(object afflictions)
    {
        if (_characterAfflictions_m_inAirport == null && afflictions != null)
            _characterAfflictions_m_inAirport = afflictions.GetType().GetField("m_inAirport", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return _characterAfflictions_m_inAirport != null && (bool)_characterAfflictions_m_inAirport.GetValue(afflictions);
    }

    // FogSphere - get fogPoint field
    private static FieldInfo _fogSphere_fogPoint;
    public static Vector3 GetFogPoint(object sphere)
    {
        if (_fogSphere_fogPoint == null && sphere != null)
            _fogSphere_fogPoint = sphere.GetType().GetField("fogPoint", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return _fogSphere_fogPoint != null ? (Vector3)_fogSphere_fogPoint.GetValue(sphere) : Vector3.zero;
    }
}