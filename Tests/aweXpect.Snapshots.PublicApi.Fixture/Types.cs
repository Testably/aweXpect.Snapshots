using System;
using System.Collections.Generic;

namespace aweXpect.Snapshots.PublicApi.Fixture;

public abstract class Album : IComparable<Album>
{
	public const int MaxTracks = 99;
	public static readonly Album? Empty = null;

	protected Album(string title)
	{
		Title = title;
	}

	public string Title { get; }
	public string? Note { get; set; }
	public string this[int track] => Title;

	public event EventHandler? Played;

	public int CompareTo(Album? other) => 0;
	public abstract IEnumerable<string> Tracks();
	public virtual void Play(params IEnumerable<string> tracks) => Played?.Invoke(this, EventArgs.Empty);
	protected internal virtual bool TryGetTrack(int number, out string? track, ref int position, in DateTime at)
	{
		track = null;
		return false;
	}

	internal void Hidden() { }

	public static bool operator ==(Album? left, Album? right) => false;
	public static bool operator !=(Album? left, Album? right) => true;
	public static explicit operator string(Album album) => album.Title;

	public override bool Equals(object? obj) => false;
	public override int GetHashCode() => 0;

	public sealed class Track
	{
		public int Number { get; init; }
	}
}

[Flags]
public enum Genre : byte
{
	None = 0,
	Rock = 1,
	Jazz = 2,
	All = Rock | Jazz,
}

public interface IPlayer<in TAlbum> where TAlbum : Album
{
	string Name { get; }
	void Play(TAlbum album, bool shuffle = false);
}

public delegate TResult Converter<in TSource, out TResult>(TSource source) where TSource : notnull;

public record Artist(string Name, Genre Genre = Genre.Rock);

public readonly record struct Rating(int Stars);

public readonly struct Duration
{
	public Duration(int seconds)
	{
		Seconds = seconds;
	}

	public int Seconds { get; }
#if NET
	public ReadOnlySpan<char> Format(scoped ReadOnlySpan<char> pattern) => default;
#endif
}

public static class Library
{
	public static T? Find<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : class, new() => null;
	public static void Reset<T>(ref T value) where T : unmanaged { }

	[Obsolete("Use Find instead.")]
	public static object? Search(string query) => null;
}
