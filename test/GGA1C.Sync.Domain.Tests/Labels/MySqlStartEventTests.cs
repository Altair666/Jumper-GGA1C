using System;
using Shouldly;
using Xunit;

namespace GGA1C.Sync.Labels;

/// <summary>
/// БАГ-1.2: фильтр журнала System для события «служба MySQL перешла в состояние "работает"».
/// Тот же фильтр на стенде находит запуски MySQL 18.10 и 25.10 (Get-WinEvent -FilterXPath).
/// </summary>
public class MySqlStartEventTests
{
    [Fact]
    public void Filter_Is_Service_Control_Manager_7036_For_That_Service_Running()
    {
        MySqlStartEvent.XPath("MySQL").ShouldBe(
            "*[System[Provider[@Name='Service Control Manager'] and (EventID=7036)]]"
            + " and *[EventData[Data[@Name='param1']='MySQL'] and EventData[Data[@Name='param2']='running']]");
    }

    [Theory]
    [InlineData("")]
    [InlineData("My'SQL")]
    [InlineData("a<b")]
    public void Bad_Service_Name_Is_Rejected(string name)
    {
        Should.Throw<ArgumentException>(() => MySqlStartEvent.XPath(name));
    }
}
