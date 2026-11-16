using Qsearch.Windows.Usn;

namespace Qsearch.Windows.Tests;

public sealed class PathGraphTests
{
    [Fact]
    public void FolderRename_InvalidatesDescendantPathWithoutRewritingChildren()
    {
        PathGraph graph = new(@"C:\");
        graph.Apply(Record(1, 1, "", directory: true));
        graph.Apply(Record(2, 1, "old", directory: true));
        graph.Apply(Record(3, 2, "file.txt"));
        Assert.True(graph.TryResolvePath(new FileReferenceId(3), out string oldPath));

        graph.Apply(Record(2, 1, "new", directory: true));

        Assert.Equal(@"C:\old\file.txt", oldPath);
        Assert.True(graph.TryResolvePath(new FileReferenceId(3), out string newPath));
        Assert.Equal(@"C:\new\file.txt", newPath);
    }

    [Fact]
    public void RepeatedReference_UsesOneCanonicalParent()
    {
        PathGraph graph = new(@"C:\");
        graph.Apply(Record(1, 1, "", directory: true));
        graph.Apply(Record(2, 1, "first", directory: true));
        graph.Apply(Record(3, 1, "second", directory: true));
        graph.Apply(Record(4, 2, "linked.txt"));
        graph.Apply(Record(4, 3, "linked.txt"));

        Assert.True(graph.TryResolvePath(new FileReferenceId(4), out string path));
        Assert.Equal(@"C:\second\linked.txt", path);
    }

    private static UsnRecord Record(ulong id, ulong parent, string name, bool directory = false)
    {
        return new UsnRecord(
            new FileReferenceId(id),
            new FileReferenceId(parent),
            1,
            UsnReason.FileCreate,
            directory ? FileAttributes.Directory : FileAttributes.Normal,
            name,
            2);
    }
}
