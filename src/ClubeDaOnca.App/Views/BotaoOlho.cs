namespace ClubeDaOnca.App.Views;

/// <summary>
/// Botão de "olho" ao lado de um campo de senha: alterna entre mostrar e esconder o texto digitado.
/// Uso: &lt;views:BotaoOlho Campo="{x:Reference SenhaEntry}" /&gt;
/// </summary>
public sealed class BotaoOlho : Button
{
    private const string Mostrar = "👁";
    private const string Esconder = "🙈";

    public static readonly BindableProperty CampoProperty =
        BindableProperty.Create(nameof(Campo), typeof(Entry), typeof(BotaoOlho));

    public Entry? Campo
    {
        get => (Entry?)GetValue(CampoProperty);
        set => SetValue(CampoProperty, value);
    }

    public BotaoOlho()
    {
        Text = Mostrar;
        FontSize = 18;
        Padding = new Thickness(8, 0);
        MinimumWidthRequest = 44;
        BackgroundColor = Colors.Transparent;
        VerticalOptions = LayoutOptions.Center;
        SemanticProperties.SetDescription(this, "Mostrar senha");
        Clicked += (_, _) => Alternar();
    }

    private void Alternar()
    {
        if (Campo is null) return;
        Campo.IsPassword = !Campo.IsPassword;
        Text = Campo.IsPassword ? Mostrar : Esconder;
        SemanticProperties.SetDescription(this, Campo.IsPassword ? "Mostrar senha" : "Esconder senha");
    }
}
