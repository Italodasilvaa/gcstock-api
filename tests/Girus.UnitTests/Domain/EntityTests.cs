using Girus.Domain.Common;

namespace Girus.UnitTests.Domain;

file sealed class SampleEntity(Guid id) : Entity<Guid>(id);

public class EntityTests
{
    [Fact]
    public void Entities_With_Same_Id_And_Type_Are_Equal()
    {
        var id = Guid.NewGuid();
        var first = new SampleEntity(id);
        var second = new SampleEntity(id);

        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Entities_With_Different_Ids_Are_Not_Equal()
    {
        var first = new SampleEntity(Guid.NewGuid());
        var second = new SampleEntity(Guid.NewGuid());

        Assert.NotEqual(first, second);
    }
}
