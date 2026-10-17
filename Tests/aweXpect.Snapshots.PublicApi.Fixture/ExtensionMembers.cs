using System.Collections.Generic;

namespace aweXpect.Snapshots.PublicApi.Fixture;

public static class ExtensionMembers
{
	extension(string text)
	{
		public bool IsBlank => string.IsNullOrWhiteSpace(text);
		public bool IsLongerThan(int length = 10) => text.Length > length;
	}

	extension(string)
	{
		public static string Default
		{
			get => _default;
			set => _default = value;
		}

		public static string Create() => "";
	}

	private static string _default = "";

	extension<T>(IEnumerable<T> source) where T : class
	{
		public T? FirstOrNothing() => null;
	}

	public static bool IsOld(this string text) => false;
}
