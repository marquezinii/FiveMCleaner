using System.Windows;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Ponto único de decisão para a linguagem de movimento do app: se o Windows
/// pede menos animação (Configurações de Acessibilidade), toda transição
/// estrutural/de navegação vira instantânea. Animações de estado real (o
/// núcleo 3D, a barra de progresso) continuam obedecendo às próprias regras
/// de <c>IsLive</c>, que já as param fora de tela.
/// </summary>
public static class MotionPolicy
{
    public static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;
}
