namespace aweXpect.Snapshots;

/// <summary>
///     What a <see cref="SnapshotComparer" /> compares, see <see cref="SnapshotComparer.CompareAsync" />.
/// </summary>
public sealed class SnapshotComparisonContext
{
	internal SnapshotComparisonContext(SnapshotId id, SnapshotContent snapshot, SnapshotContent actual)
	{
		Id = id;
		Snapshot = snapshot;
		Actual = actual;
	}

	/// <summary>
	///     The value that is compared with the snapshot, after the rules of the expectation were applied, e.g. with
	///     masked GUIDs.
	/// </summary>
	public SnapshotContent Actual { get; }

	/// <summary>
	///     The id of the snapshot, e.g. with its <see cref="SnapshotId.Extension" />.
	/// </summary>
	public SnapshotId Id { get; }

	/// <summary>
	///     The reviewed snapshot.
	/// </summary>
	/// <remarks>
	///     When a snapshot is used again in the same test run for a value with other bytes, it is the first value of the
	///     test run instead, so that the comparer decides whether both are the same value.
	/// </remarks>
	public SnapshotContent Snapshot { get; }
}
