using System;
using Avalonia;
using Avalonia.Controls;

namespace FFXProjectEditor.Styles
{
	/// <summary>
	/// Infra de responsividade (Jarvis-UI). Marca a raiz de um controle com as classes
	/// <c>narrow</c> / <c>medium</c> / <c>wide</c> conforme a largura REAL renderizada,
	/// para que Selectors de Style funcionem como "media queries" do CSS.
	///
	/// Uso no XAML (UserControl raiz):
	///   xmlns:rsp="clr-namespace:FFXProjectEditor.Styles"
	///   rsp:Responsive.Breakpoints="True"
	/// e nos &lt;UserControl.Styles&gt;:
	///   &lt;Style Selector="UserControl.narrow UniformGrid.cards"&gt;&lt;Setter Property="Columns" Value="1"/&gt;&lt;/Style&gt;
	///
	/// Desempenho é intencionalmente ignorado (decisão do dono): a assinatura de Bounds
	/// vive enquanto o controle existir; só reaplica classes quando a faixa muda.
	/// </summary>
	public static class Responsive
	{
		// Limiares (px de largura). Ajuste fino aqui afeta todas as telas que optarem por Breakpoints.
		public const double NarrowMax = 760;
		public const double MediumMax = 1180;

		public static readonly AttachedProperty<bool> BreakpointsProperty =
			AvaloniaProperty.RegisterAttached<Control, bool>("Breakpoints", typeof(Responsive));

		public static void SetBreakpoints(Control control, bool value) => control.SetValue(BreakpointsProperty, value);
		public static bool GetBreakpoints(Control control) => control.GetValue(BreakpointsProperty);

		static Responsive()
		{
			BreakpointsProperty.Changed.AddClassHandler<Control>((control, e) =>
			{
				if (e.NewValue is true)
				{
					// Aplica já com o tamanho atual e reaplica a cada mudança de Bounds.
					Apply(control, control.Bounds.Width);
					control.GetObservable(Visual.BoundsProperty).Subscribe(new BoundsObserver(control));
				}
			});
		}

		private static void Apply(Control control, double width)
		{
			// width 0 acontece antes do primeiro layout; trata como "wide" para não esconder nada cedo demais.
			bool narrow = width > 0 && width < NarrowMax;
			bool medium = width >= NarrowMax && width < MediumMax;
			bool wide = width <= 0 || width >= MediumMax;

			SetClass(control, "narrow", narrow);
			SetClass(control, "medium", medium);
			SetClass(control, "wide", wide);
		}

		private static void SetClass(Control control, string name, bool on)
		{
			bool has = control.Classes.Contains(name);
			if (on && !has) control.Classes.Add(name);
			else if (!on && has) control.Classes.Remove(name);
		}

		// IObserver leve para a faixa de largura (evita capturar lambdas a cada tick).
		private sealed class BoundsObserver : IObserver<Rect>
		{
			private readonly Control _control;
			public BoundsObserver(Control control) => _control = control;
			public void OnCompleted() { }
			public void OnError(Exception error) { }
			public void OnNext(Rect value) => Apply(_control, value.Width);
		}
	}
}
