using GCStock.Domain.Services;

namespace GCStock.UnitTests.Domain;

public class SalesGoalTests
{
    [Fact]
    public void Meta_de_3000_com_1200_realizados_mostra_percentual_e_quanto_falta()
    {
        var progress = SalesGoal.Compute(3000m, 1200m, new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12));

        Assert.Equal((40m, 1800m), (progress.PercentAchieved, progress.Remaining));
    }

    [Fact]
    public void Projecao_do_mes_corrente_extrapola_o_ritmo_dos_dias_passados()
    {
        // 1.200 em 12 dias de um mês de 30 dias => 3.000.
        var progress = SalesGoal.Compute(3000m, 1200m, new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12));

        Assert.Equal(3000m, progress.Projection);
        Assert.Equal((12, 30), (progress.DaysElapsed, progress.DaysInMonth));
    }

    [Fact]
    public void Mes_passado_projeta_o_proprio_realizado_e_mes_futuro_nao_projeta()
    {
        Assert.Equal(2500m, SalesGoal.Compute(3000m, 2500m, new DateOnly(2026, 10, 5), new DateOnly(2026, 11, 12)).Projection);
        Assert.Equal(0m, SalesGoal.Compute(3000m, 0m, new DateOnly(2026, 12, 5), new DateOnly(2026, 11, 12)).Projection);
    }

    [Fact]
    public void Meta_batida_nao_tem_o_que_falta_e_passa_de_100_por_cento()
    {
        var progress = SalesGoal.Compute(3000m, 3600m, new DateOnly(2026, 11, 30), new DateOnly(2026, 11, 30));

        Assert.Equal((120m, 0m), (progress.PercentAchieved, progress.Remaining));
    }

    [Fact]
    public void Sem_meta_definida_o_percentual_e_zero() =>
        Assert.Equal(0m, SalesGoal.Compute(0m, 500m, new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12)).PercentAchieved);

    [Theory]
    [InlineData(2026, 11, 12, 2026, 11, 9, 2026, 11, 15)]  // quinta => segunda 9 a domingo 15
    [InlineData(2026, 11, 9, 2026, 11, 9, 2026, 11, 15)]   // segunda
    [InlineData(2026, 11, 15, 2026, 11, 9, 2026, 11, 15)]  // domingo
    [InlineData(2026, 11, 1, 2026, 10, 26, 2026, 11, 1)]   // domingo no começo do mês
    public void Semana_vai_de_segunda_a_domingo(int y, int m, int d, int fy, int fm, int fd, int ty, int tm, int td)
    {
        var (from, to) = SalesGoal.WeekOf(new DateOnly(y, m, d));

        Assert.Equal((new DateOnly(fy, fm, fd), new DateOnly(ty, tm, td)), (from, to));
    }

    [Theory]
    [InlineData(150, 100, 50)]
    [InlineData(50, 100, -50)]
    [InlineData(0, 0, 0)]
    public void Variacao_sobre_o_periodo_anterior(double current, double previous, double expected) =>
        Assert.Equal((decimal)expected, SalesGoal.ChangePercent((decimal)current, (decimal)previous));

    [Fact]
    public void Sem_base_de_comparacao_nao_ha_percentual() =>
        Assert.Null(SalesGoal.ChangePercent(100m, 0m));
}
