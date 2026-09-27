using System;
using FFXProjectEditor.Core.LLM;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// L1 P2-B (Jarvis-CLINE 2026-07-31): aprovação humana obrigatória com expiração.
    /// Anti auto-apply: proposta aprovada não vale para sempre.
    /// </summary>
    public class PatchProposalReviewTests
    {
        private static PatchProposalReview Approved(DateTimeOffset expiresAt) => new()
        {
            ProposalId = "p-1",
            Decision = ReviewDecision.Approved,
            ReviewedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
        };

        [Fact]
        public void Review_ValidWithinExpiry()
        {
            var review = Approved(DateTimeOffset.UtcNow.AddMinutes(30));

            Assert.True(review.IsValidNow(DateTimeOffset.UtcNow));
        }

        [Fact]
        public void Review_Expired_NotValid()
        {
            var review = Approved(DateTimeOffset.UtcNow.AddMinutes(-1));

            Assert.False(review.IsValidNow(DateTimeOffset.UtcNow));
        }

        [Fact]
        public void Review_Rejected_NeverValid()
        {
            var review = Approved(DateTimeOffset.UtcNow.AddMinutes(30)) with
            {
                Decision = ReviewDecision.Rejected,
            };

            Assert.False(review.IsValidNow(DateTimeOffset.UtcNow));
        }

        [Fact]
        public void Review_Pending_NeverValid()
        {
            var review = Approved(DateTimeOffset.UtcNow.AddMinutes(30)) with
            {
                Decision = ReviewDecision.Pending,
            };

            Assert.False(review.IsValidNow(DateTimeOffset.UtcNow));
        }
    }
}
