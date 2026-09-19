using Basin;
using Waylonia.Shell.Applets;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class WorkspaceSwitcherViewModelTests
{
    [Fact]
    public void One_cell_per_workspace_in_rows_with_columns_rounded_up()
    {
        var model = PanelModelFixture.Model(workspaces: 5, rows: 2);
        var switcher = new WorkspaceSwitcherViewModel(model);

        Assert.Equal(5, switcher.Cells.Count);
        Assert.Equal(2, switcher.Rows);
        Assert.Equal(3, switcher.Columns);
        Assert.Equal(["1", "2", "3", "4", "5"], switcher.Cells.Select(cell => cell.Name));
        Assert.Equal([0, 1, 2, 3, 4], switcher.Cells.Select(cell => cell.Index));
    }

    [Fact]
    public void Rows_never_exceed_the_workspace_count()
    {
        Assert.Equal((2, 1), WorkspaceSwitcherViewModel.Grid(2, 4));
        Assert.Equal((1, 4), WorkspaceSwitcherViewModel.Grid(4, 0));
        Assert.Equal((3, 3), WorkspaceSwitcherViewModel.Grid(7, 3));
        Assert.Equal((1, 1), WorkspaceSwitcherViewModel.Grid(0, 1));
    }

    [Fact]
    public void The_current_cell_follows_the_model()
    {
        var model = PanelModelFixture.Model();
        var switcher = new WorkspaceSwitcherViewModel(model);
        Assert.Equal([true, false, false, false], switcher.Cells.Select(cell => cell.IsCurrent));

        model.CurrentWorkspace = 2;

        Assert.Equal([false, false, true, false], switcher.Cells.Select(cell => cell.IsCurrent));
    }

    [Fact]
    public void Miniatures_scale_the_frame_against_the_work_area_into_the_cell()
    {
        var model = PanelModelFixture.Model();
        model.WorkArea = new Box(0, 0, 800, 600);
        model.Windows =
        [
            PanelModelFixture.Window(1, "Editor", focused: true, frame: new Box(200, 150, 400, 300)),
            PanelModelFixture.Window(2, "Hidden", minimized: true, frame: new Box(0, 0, 800, 600)),
            PanelModelFixture.Window(3, "Player", workspace: 1, sticky: true, frame: new Box(0, 0, 80, 60)),
            PanelModelFixture.Window(4, "Mail", workspace: 1, frame: new Box(400, 0, 400, 600)),
        ];
        var switcher = new WorkspaceSwitcherViewModel(model);

        Assert.Equal(40, switcher.CellWidth);
        Assert.Equal(20, switcher.CellHeight);
        Assert.Equal(
            [new WorkspaceMiniatureViewModel(1, 10, 5, 20, 10, true), new WorkspaceMiniatureViewModel(3, 0, 0, 4, 2, false)],
            switcher.Cells[0].Windows);
        Assert.Equal(
            [new WorkspaceMiniatureViewModel(3, 0, 0, 4, 2, false), new WorkspaceMiniatureViewModel(4, 20, 0, 20, 20, false)],
            switcher.Cells[1].Windows);
        Assert.Equal([new WorkspaceMiniatureViewModel(3, 0, 0, 4, 2, false)], switcher.Cells[2].Windows);
    }

    [Fact]
    public void The_work_area_origin_is_subtracted_and_the_cell_size_rescales()
    {
        var model = PanelModelFixture.Model();
        model.WorkArea = new Box(0, 24, 800, 552);
        model.Windows = [PanelModelFixture.Window(1, "Editor", frame: new Box(400, 24, 400, 552))];
        var switcher = new WorkspaceSwitcherViewModel(model);

        Assert.Equal(new WorkspaceMiniatureViewModel(1, 20, 0, 20, 20, false), Assert.Single(switcher.Cells[0].Windows));

        switcher.CellWidth = 80;
        switcher.CellHeight = 40;

        Assert.Equal(80, switcher.Cells[0].Width);
        Assert.Equal(40, switcher.Cells[0].Height);
        Assert.Equal(new WorkspaceMiniatureViewModel(1, 40, 0, 40, 40, false), Assert.Single(switcher.Cells[0].Windows));
    }

    [Fact]
    public void A_tiny_window_still_shows_as_one_pixel()
    {
        var miniature = WorkspaceSwitcherViewModel.Miniature(
            PanelModelFixture.Window(1, frame: new Box(0, 0, 2, 2)), new Box(0, 0, 800, 600), 40, 20);

        Assert.Equal(1, miniature.Width);
        Assert.Equal(1, miniature.Height);
    }

    [Fact]
    public void Switch_and_drop_reach_the_commands()
    {
        var commands = new FakePanelCommands();
        var switcher = new WorkspaceSwitcherViewModel(PanelModelFixture.Model(commands));

        switcher.Cells[2].SwitchCommand.Execute(null);
        switcher.SwitchCommand.Execute(1);
        switcher.DropWindow(7, 3);

        Assert.Equal(["SwitchWorkspace 2", "SwitchWorkspace 1", "MoveToWorkspace 7 3"], commands.Calls);
    }

    [Fact]
    public void Changing_the_workspace_list_keeps_matching_cells()
    {
        var model = PanelModelFixture.Model(workspaces: 2);
        var switcher = new WorkspaceSwitcherViewModel(model);
        var first = switcher.Cells[0];

        model.Workspaces = [new(0, "Main"), new(1, "Mail"), new(2, "3")];

        Assert.Same(first, switcher.Cells[0]);
        Assert.Equal(["Main", "Mail", "3"], switcher.Cells.Select(cell => cell.Name));
        Assert.Equal(3, switcher.Columns);
    }
}
