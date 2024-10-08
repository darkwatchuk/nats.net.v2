using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
#if NETSTANDARD
using Random = NATS.Client.Core.Internal.NetStandardExtensions.Random;
#endif

namespace NATS.Client.Core;

/// <summary>
/// Represents a unique identifier generator.
/// </summary>
/// <remarks>
/// The <c>Nuid</c> class generates unique identifiers that can be used
/// to ensure uniqueness in distributed systems.
/// </remarks>
[SkipLocalsInit]
public sealed class Nuid
{
    // NuidLength, PrefixLength, SequentialLength were nuint (System.UIntPtr) in the original code
    // however, they were changed to uint to fix the compilation error for IL2CPP Unity projects.
    // With nuint, the following error occurs in Unity Linux IL2CPP builds:
    //   Error: IL2CPP error for method 'System.Char[] NATS.Client.Core.Internal.NuidWriter::Refresh(System.UInt64&)'
    //   System.ArgumentOutOfRangeException: Cannot create a constant value for types of System.UIntPtr
    internal const uint NuidLength = PrefixLength + SequentialLength;
    private const uint Base = 62;
    private const ulong MaxSequential = 839299365868340224; // 62^10
    private const uint PrefixLength = 12;
    private const uint SequentialLength = 10;
    private const int MinIncrement = 33;
    private const int MaxIncrement = 333;

    [ThreadStatic]
    private static Nuid? _writer;

    private char[] _prefix;
    private ulong _increment;
    private ulong _sequential;

    private Nuid()
    {
        Refresh(out _);
    }

#if NETSTANDARD2_0
    private static ReadOnlySpan<char> Digits => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".AsSpan();
#else
    private static ReadOnlySpan<char> Digits => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
#endif

    /// <summary>
    /// Generates a new NATS unique identifier (NUID).
    /// </summary>
    /// <returns>A new NUID as a string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when unable to generate the NUID.</exception>
    public static string NewNuid()
    {
        Span<char> buffer = stackalloc char[(int)NuidLength];
        if (TryWriteNuid(buffer))
        {
            return buffer.ToString();
        }

        throw new InvalidOperationException("Internal error: can't generate nuid");
    }

    internal static bool TryWriteNuid(Span<char> nuidBuffer)
    {
        if (_writer is not null)
        {
            return _writer.TryWriteNuidCore(nuidBuffer);
        }

        return InitAndWrite(nuidBuffer);
    }

    private static bool TryWriteNuidCore(Span<char> buffer, Span<char> prefix, ulong sequential)
    {
        if ((uint)buffer.Length < NuidLength || prefix.Length != PrefixLength)
        {
            return false;
        }

        Unsafe.CopyBlockUnaligned(ref Unsafe.As<char, byte>(ref buffer[0]), ref Unsafe.As<char, byte>(ref prefix[0]), PrefixLength * sizeof(char));

        // NOTE: We must never write to digitsPtr!
        ref var digitsPtr = ref MemoryMarshal.GetReference(Digits);

        // write backwards so the last two characters change the fastest
        for (var i = NuidLength; i > PrefixLength;)
        {
            i--;
            var digitIndex = (nuint)(sequential % Base);
            Unsafe.Add(ref buffer[0], i) = Unsafe.Add(ref digitsPtr, digitIndex);
            sequential /= Base;
        }

        return true;
    }

    private static uint GetIncrement()
    {
        return (uint)Random.Shared.Next(MinIncrement, MaxIncrement + 1);
    }

    private static ulong GetSequential()
    {
        return (ulong)Random.Shared.NextInt64(0, (long)MaxSequential + 1);
    }

    private static char[] GetPrefix(RandomNumberGenerator? rng = null)
    {
#if NET8_0_OR_GREATER
        if (rng == null)
        {
            return RandomNumberGenerator.GetItems(Digits, (int)PrefixLength);
        }
#endif

#if NETSTANDARD2_0
        var randomBytes = new byte[(int)PrefixLength];

        if (rng == null)
        {
            using var randomNumberGenerator = RandomNumberGenerator.Create();
            randomNumberGenerator.GetBytes(randomBytes);
        }
#else
        Span<byte> randomBytes = stackalloc byte[(int)PrefixLength];
        if (rng == null)
        {
            RandomNumberGenerator.Fill(randomBytes);
        }
#endif
        else
        {
            rng.GetBytes(randomBytes);
        }

        var newPrefix = new char[PrefixLength];

        for (var i = 0; i < randomBytes.Length; i++)
        {
            var digitIndex = (int)(randomBytes[i] % Base);
            newPrefix[i] = Digits[digitIndex];
        }

        return newPrefix;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InitAndWrite(Span<char> span)
    {
        _writer = new Nuid();
        return _writer.TryWriteNuidCore(span);
    }

    private bool TryWriteNuidCore(Span<char> nuidBuffer)
    {
        var sequential = _sequential += _increment;

        if (sequential < MaxSequential)
        {
            return TryWriteNuidCore(nuidBuffer, _prefix, sequential);
        }

        return RefreshAndWrite(nuidBuffer);

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool RefreshAndWrite(Span<char> buffer)
        {
            var prefix = Refresh(out sequential);
            return TryWriteNuidCore(buffer, prefix, sequential);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [MemberNotNull(nameof(_prefix))]
    private char[] Refresh(out ulong sequential)
    {
        var prefix = _prefix = GetPrefix();
        _increment = GetIncrement();
        sequential = _sequential = GetSequential();
        return prefix;
    }
}
