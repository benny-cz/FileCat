using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class StorageVisitLedgerTests
{
    private static StorageObjectIdentity Identity(int number, string binding = "volume-one") =>
        new(7, number.ToString("X32")) { VolumeBinding = binding };

    [Fact]
    public void A_closed_identity_is_a_hint_and_never_a_new_admission_or_completed_subtree()
    {
        var ledger = new StorageVisitLedger(); var identity = Identity(1);
        Assert.Equal(StorageVisitAdmission.New, ledger.Admit(identity, 3, out int receipt));
        ledger.Settle(receipt, StorageEffect.Changed);
        Assert.Equal(StorageVisitAdmission.LiveDuplicate, ledger.Admit(identity, 4, out int same)); Assert.Equal(receipt, same);
        Assert.Equal(new(3, StorageEffect.Changed, true, false), ledger.Read(receipt));
        ledger.Close(receipt, subtreeComplete: false);
        Assert.Equal(StorageVisitAdmission.ClosedDuplicate, ledger.Admit(identity, 4, out same));
        Assert.Equal(new(3, StorageEffect.Changed, false, false), ledger.Read(same));
        Assert.Equal(1, ledger.Count); Assert.Equal(3, ledger.Observations);
        Assert.Throws<InvalidOperationException>(() => ledger.Settle(receipt, StorageEffect.AlreadySatisfied));
        Assert.Throws<InvalidOperationException>(() => ledger.Close(receipt, true));
    }

    [Fact]
    public void Cloned_serials_opaque_case_and_exact_native_ids_retain_separate_keys()
    {
        var ledger = new StorageVisitLedger();
        Assert.Equal(StorageVisitAdmission.New, ledger.Admit(Identity(1), 1, out _));
        Assert.Equal(StorageVisitAdmission.New, ledger.Admit(Identity(1, "volume-clone"), 1, out _));
        Assert.Equal(StorageVisitAdmission.New, ledger.Admit(new(7, "case-name"), 1, out _));
        Assert.Equal(StorageVisitAdmission.New, ledger.Admit(new(7, "CASE-NAME"), 1, out _));
        Assert.Equal(StorageVisitAdmission.New, ledger.Admit(new(7, new string('a', 32)), 1, out _));
        Assert.Equal(StorageVisitAdmission.New, ledger.Admit(new(7, new string('A', 32)), 1, out _));
        Assert.Equal(6, ledger.Count);
    }

    [Fact]
    public void Duplicate_observations_consume_the_count_limit_without_growing_or_reusing_outcomes()
    {
        var ledger = new StorageVisitLedger(limit: 2);
        Assert.Equal(StorageVisitAdmission.New, ledger.Admit(Identity(1), 1, out int receipt));
        long charged = ledger.RetainedBytes;
        ledger.Settle(receipt, StorageEffect.Failed); ledger.Close(receipt, false);
        Assert.Equal(StorageVisitAdmission.ClosedDuplicate, ledger.Admit(Identity(1), 1, out _));
        Assert.Equal(StorageVisitAdmission.Limit, ledger.Admit(Identity(1), 1, out int denied));
        Assert.Equal(-1, denied); Assert.Equal(2, ledger.Observations); Assert.Equal(charged, ledger.RetainedBytes);
    }

    [Fact]
    public void Chunk_and_opaque_name_capacity_is_charged_before_insertion_and_stops_at_the_byte_limit()
    {
        const long cap = 512 * 1024;
        var ledger = new StorageVisitLedger(byteLimit: cap); int admitted = 0;
        while (true)
        {
            var result = ledger.Admit(new(7, admitted.ToString() + new string('z', 240)), 1, out int receipt);
            if (result == StorageVisitAdmission.Limit) { Assert.Equal(-1, receipt); break; }
            Assert.Equal(StorageVisitAdmission.New, result); admitted++;
            ledger.Settle(receipt, StorageEffect.AlreadySatisfied); ledger.Close(receipt, true);
        }
        Assert.InRange(admitted, 1, 1024); Assert.InRange(ledger.RetainedBytes, 1, cap);
        Assert.Equal(admitted, ledger.Count); Assert.Equal(admitted, ledger.Observations);
    }

    [Fact]
    public void A_million_native_identity_observations_fit_the_retained_budget_and_the_next_admission_is_refused()
    {
        var ledger = new StorageVisitLedger();
        for (int i = 0; i < StorageVisitLedger.ObservationLimit; i++)
        {
            Assert.Equal(StorageVisitAdmission.New, ledger.Admit(Identity(i), 1, out int receipt));
            ledger.Settle(receipt, StorageEffect.AlreadySatisfied); ledger.Close(receipt, true);
        }
        Assert.Equal(StorageVisitAdmission.Limit, ledger.Admit(Identity(int.MaxValue), 1, out int refused)); Assert.Equal(-1, refused);
        Assert.Equal(StorageVisitLedger.ObservationLimit, ledger.Observations); Assert.Equal(ledger.Observations, ledger.Count);
        Assert.InRange(ledger.RetainedBytes, 1, StorageVisitLedger.ByteLimit);
    }
}
