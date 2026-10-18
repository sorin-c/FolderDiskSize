using System.Globalization;
using FolderDiskSize.Core.Formatting;

namespace FolderDiskSize.Core.Tests;

public class FormatterTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1023, "1,023 bytes")]
    [InlineData(4_970_250, "4.74 MB")]
    [InlineData(876_173_328_384, "816 GB")]
    [InlineData(1_176_480_448_512, "1.07 TB")]
    [InlineData(153_092_096, "146 MB")]
    public void Formats_sizes_like_FolderSizes(long bytes, string expected) =>
        Assert.Equal(expected, SizeFormatter.Format(bytes, CultureInfo.InvariantCulture));

    [Fact]
    public void Formats_attributes_as_letters() =>
        Assert.Equal("HSD", AttributeFormatter.Format(FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory));
}
