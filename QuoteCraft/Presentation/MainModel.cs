namespace QuoteCraft.Presentation;

public partial record MainModel
{
    private readonly INavigator _navigator;

    public MainModel(INavigator navigator)
    {
        _navigator = navigator;
    }
}
