using System.Text.RegularExpressions;

namespace Dashboard
{
	public static partial class KnownRegexes
	{

		[GeneratedRegex(@"Duration: ([\d:\.]+), start: -?[\d\.]+, bitrate: \d+")]
		public static partial Regex MetadataDuration();

		[GeneratedRegex(@"Stream #\d+:\d+(\[\w+\])?(\(\w+\))?: Video")]
		public static partial Regex MetadataVideoStreamHead();

	}
}