namespace ZoombiniHelper;

/// <summary>
/// Static virtual addresses for Fleens! puzzle state.
///
/// <para><b>Hypothesis (Probable, not yet live-verified)</b>: the three words
/// at <see cref="SpecialIndex1"/>..<see cref="SpecialIndex3"/> hold the
/// 1-based pool indices of the three "boss" fleens that the player must
/// match to win. This corresponds to <c>DS[0x7cb4]/0x7cb6/0x7cb8</c> in the
/// v1 reverse-engineering doc.</para>
///
/// <para>Six dumps from 2026-04-28 show the expected behaviour: values are
/// always in [1..16], all three differ within one round, and they change
/// per round (random). One open question is which "pool order" the indices
/// reference — engine-list order (head-to-tail), y-sorted, or some other
/// scheme. The renderer warns about this until verified live.</para>
/// </summary>
public static class FleensMemoryMap
{
    public const nint SpecialIndex1 = 0x00495C18;
    public const nint SpecialIndex2 = 0x00495C1A;
    public const nint SpecialIndex3 = 0x00495C1C;

    /// <summary>Pointer to the heap-allocated shared-state struct that
    /// holds all per-zoombini data (incl. attributes, alive flag, the
    /// per-round permutation). Reverse-engineered from the setup function
    /// at 0x00413B82 which loads <c>mov eax, dword ptr [0x4a2818]</c>
    /// and indexes <c>[eax + 0xb83c + di*0x14]</c> for ZB <c>di</c>.</summary>
    public const nint SharedStatePtr = 0x004A2818;

    /// <summary>Within the shared-state struct: per-zoombini record stride
    /// (0x14 bytes) and the offset where ZB <c>di</c>'s attribute bytes
    /// start (Hair, Eyes, Nose, Feet at +0..+3).</summary>
    public const int  ZbStride       = 0x14;
    public const int  ZbAttrsOffset  = 0xB83C;
}
