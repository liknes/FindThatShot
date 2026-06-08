// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using VideoArchiveManager.Core.Services.Ai;
using Xunit;

namespace VideoArchiveManager.Core.Tests;

public class VectorMathTests
{
    [Fact]
    public void L2NormalizeInPlace_yields_unit_length()
    {
        var v = new[] { 3f, 4f };
        VectorMath.L2NormalizeInPlace(v);

        var length = MathF.Sqrt(v[0] * v[0] + v[1] * v[1]);
        length.Should().BeApproximately(1f, 1e-5f);
        v[0].Should().BeApproximately(0.6f, 1e-5f);
        v[1].Should().BeApproximately(0.8f, 1e-5f);
    }

    [Fact]
    public void L2NormalizeInPlace_leaves_zero_vector_untouched()
    {
        var v = new[] { 0f, 0f, 0f };
        VectorMath.L2NormalizeInPlace(v);
        v.Should().OnlyContain(x => x == 0f);
    }

    [Fact]
    public void Dot_of_unit_vectors_is_cosine_similarity()
    {
        var a = new[] { 1f, 0f };
        var b = new[] { 0f, 1f };
        VectorMath.Dot(a, b).Should().BeApproximately(0f, 1e-6f);

        var c = new[] { 1f, 0f };
        VectorMath.Dot(a, c).Should().BeApproximately(1f, 1e-6f);
    }

    [Fact]
    public void ToBytes_FromBytes_round_trips()
    {
        var v = new[] { 0.1f, -0.5f, 12.25f, 0f };
        var bytes = VectorMath.ToBytes(v);
        var restored = VectorMath.FromBytes(bytes);

        restored.Should().Equal(v);
    }

    [Fact]
    public void MeanNormalized_pools_then_normalizes()
    {
        var pooled = VectorMath.MeanNormalized(new[]
        {
            new[] { 1f, 0f },
            new[] { 0f, 1f },
        });

        // Mean is (0.5, 0.5); normalized is (0.707, 0.707).
        var length = MathF.Sqrt(pooled[0] * pooled[0] + pooled[1] * pooled[1]);
        length.Should().BeApproximately(1f, 1e-5f);
        pooled[0].Should().BeApproximately(pooled[1], 1e-6f);
    }
}
