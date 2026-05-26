using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Attached to BOTH the Survivor and Monster player prefabs.
///
/// Holds:
/// - SpeedMultiplier (NetworkVariable, server-write) — read by MainPlayerScript.Move().
/// - ApplySpeedBoost (server-only) — used by PowerupOrb when a survivor grabs an orb.
/// - RevealClientRpc (server -> monster's client only) — applies highlight emission on this
///   player's currently-visible mesh, so the monster sees survivors glow for a few seconds.
/// - TeleportAndLockClientRpc (server -> owner only) — used by RoundManager to slam the monster
///   to the lock position during SurvivorHide, then restore on Active. Uses NetworkTransform.Teleport
///   to avoid interpolation artifacts on remote views.
/// </summary>
public class PlayerPowerupReceiver : NetworkBehaviour
{
    [Header("Reveal Visual")]
    [Tooltip("Emission color applied to the player's visible mesh when monster picks up an orb.")]
    public Color revealEmissionColor = new Color(1.5f, 0.2f, 0.2f, 1f); // HDR red

    // Default 1.0 = normal speed. Server sets to 1.5 for 5s when survivor grabs an orb.
    public NetworkVariable<float> SpeedMultiplier = new NetworkVariable<float>(
        1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Coroutine _speedBoostCoroutine;
    private Coroutine _revealCoroutine;

    // ---- Speed boost (server only) ----
    public void ApplySpeedBoost(float multiplier, float duration)
    {
        if (!IsServer) return;
        if (_speedBoostCoroutine != null) StopCoroutine(_speedBoostCoroutine);
        _speedBoostCoroutine = StartCoroutine(SpeedBoostRoutine(multiplier, duration));
    }

    private IEnumerator SpeedBoostRoutine(float multiplier, float duration)
    {
        SpeedMultiplier.Value = multiplier;
        yield return new WaitForSeconds(duration);
        SpeedMultiplier.Value = 1f;
        _speedBoostCoroutine = null;
    }

    // ---- Monster reveal of survivors (server invokes targeted to monster only) ----
    [ClientRpc]
    public void RevealClientRpc(float duration, ClientRpcParams rpcParams = default)
    {
        // Targeted ClientRpc — only the monster's client runs this body.
        if (_revealCoroutine != null) StopCoroutine(_revealCoroutine);
        _revealCoroutine = StartCoroutine(RevealRoutine(duration));
    }

    private IEnumerator RevealRoutine(float duration)
    {
        // Find the currently-visible renderer(s) on this player.
        // Could be playerVisualBody (human form) OR the active child of propVisualContainer (prop form).
        var mainScript = GetComponent<MainPlayerScript>();
        List<Renderer> targets = new List<Renderer>();

        if (mainScript != null)
        {
            // Human-form body
            if (mainScript.playerVisualBody != null && mainScript.playerVisualBody.gameObject.activeInHierarchy)
            {
                CollectRenderers(mainScript.playerVisualBody, targets);
            }
            // Prop-form: whichever child is active
            if (mainScript.propVisualContainer != null)
            {
                foreach (Transform child in mainScript.propVisualContainer)
                {
                    if (child.gameObject.activeInHierarchy) CollectRenderers(child, targets);
                }
            }
        }

        // Fallback: if nothing collected (e.g. mid-transformation), grab any renderer under us.
        if (targets.Count == 0)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(false))
                if (r != null) targets.Add(r);
        }

        // Cache original emission state per material instance and apply the glow.
        // Note: assigning material[] indices returns instances — they're scoped to this Renderer,
        // so we don't have to worry about leaking the change to other players.
        var saved = new List<(Material mat, Color color, bool keywordOn)>();
        foreach (var r in targets)
        {
            foreach (var mat in r.materials) // materials (not sharedMaterials) -> instance copies
            {
                if (mat == null) continue;
                if (!mat.HasProperty("_EmissionColor")) continue;
                Color origColor = mat.GetColor("_EmissionColor");
                bool origKeyword = mat.IsKeywordEnabled("_EMISSION");
                saved.Add((mat, origColor, origKeyword));

                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", revealEmissionColor);
            }
        }

        yield return new WaitForSeconds(duration);

        // Restore.
        foreach (var entry in saved)
        {
            if (entry.mat == null) continue;
            entry.mat.SetColor("_EmissionColor", entry.color);
            if (entry.keywordOn) entry.mat.EnableKeyword("_EMISSION");
            else entry.mat.DisableKeyword("_EMISSION");
        }
        _revealCoroutine = null;
    }

    private static void CollectRenderers(Transform root, List<Renderer> sink)
    {
        var r = root.GetComponent<Renderer>();
        if (r != null) sink.Add(r);
        foreach (var child in root.GetComponentsInChildren<Renderer>(false))
            if (child != null && child != r) sink.Add(child);
    }

    // ---- Teleport-and-lock RPC (server -> owner only, for monster lock during SurvivorHide) ----
    // §6.3 / §12.16: ClientNetworkTransform is owner-authoritative, so the server can't set
    // transform.position directly. We have the owner do it, using NetworkTransform.Teleport()
    // which signals remotes to skip interpolation (no "slide through the floor" visual).
    [ClientRpc]
    public void TeleportAndLockClientRpc(Vector3 pos, bool locked, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        // Use NetworkTransform.Teleport when available so remotes don't interpolate the jump.
        var nt = GetComponent<NetworkTransform>();
        if (nt != null)
        {
            nt.Teleport(pos, transform.rotation, transform.localScale);
        }
        else
        {
            transform.position = pos;
        }

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = locked;
        }
    }
}
