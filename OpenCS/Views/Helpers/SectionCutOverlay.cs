using OpenCS.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.Views.Helpers
{
    /// <summary>
    /// Прозрачный слой поверх FiberCanvas для указания линии разреза сечения.
    /// Активен только когда CutViewModel.IsActive == true (см. IsHitTestVisible-биндинг в XAML) —
    /// иначе клики/зум/пан беспрепятственно проходят к FiberCanvas снизу.
    /// </summary>
    public class SectionCutOverlay : FrameworkElement
    {
        const double HandleRadius = 5.0;
        const double HandleHitRadius = 10.0;

        public static readonly DependencyProperty CutViewModelProperty =
            DependencyProperty.Register(nameof(CutViewModel), typeof(SectionCutVM),
                typeof(SectionCutOverlay), new FrameworkPropertyMetadata(null, OnCutViewModelChanged));

        public SectionCutVM? CutViewModel
        {
            get => (SectionCutVM?)GetValue(CutViewModelProperty);
            set => SetValue(CutViewModelProperty, value);
        }

        public static readonly DependencyProperty HostProperty =
            DependencyProperty.Register(nameof(Host), typeof(FiberCanvas),
                typeof(SectionCutOverlay), new FrameworkPropertyMetadata(null, OnHostChanged));

        public FiberCanvas? Host
        {
            get => (FiberCanvas?)GetValue(HostProperty);
            set => SetValue(HostProperty, value);
        }

        static readonly Pen _linePen = MakeFreezePen(Brushes.OrangeRed, 2.0);
        static readonly Brush _handleFill = MakeFreezeBrush(Brushes.White);
        static readonly Pen _handlePen = MakeFreezePen(Brushes.OrangeRed, 1.5);

        Point _previewScreen;
        bool _hasPreview;
        int _dragIndex = -1;

        public SectionCutOverlay()
        {
            Focusable = true;
        }

        static void OnCutViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ov = (SectionCutOverlay)d;
            if (e.OldValue is SectionCutVM old) old.Changed -= ov.OnCutChanged;
            if (e.NewValue is SectionCutVM vm) vm.Changed += ov.OnCutChanged;
        }

        static void OnHostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ov = (SectionCutOverlay)d;
            if (e.OldValue is FiberCanvas oldHost) oldHost.ViewTransformChanged -= ov.OnHostViewChanged;
            if (e.NewValue is FiberCanvas newHost) newHost.ViewTransformChanged += ov.OnHostViewChanged;
        }

        void OnCutChanged() => InvalidateVisual();
        void OnHostViewChanged() => InvalidateVisual();

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

            var vm = CutViewModel;
            var host = Host;
            if (vm == null || host == null) return;

            var result = vm.Result;
            if (result != null)
            {
                var start = host.ToScreen(new Point(result.Start.X * 1000, result.Start.Y * 1000));
                var end = host.ToScreen(new Point(result.End.X * 1000, result.End.Y * 1000));
                dc.DrawLine(_linePen, start, end);
                dc.DrawEllipse(_handleFill, _handlePen, start, HandleRadius, HandleRadius);
                dc.DrawEllipse(_handleFill, _handlePen, end, HandleRadius, HandleRadius);
            }

            if (vm.HasPendingPoint && _hasPreview)
            {
                // первая точка Free-режима уже поставлена, но результата ещё нет — рисуем только текущий курсор
                dc.DrawEllipse(_handleFill, _handlePen, _previewScreen, HandleRadius, HandleRadius);
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            var vm = CutViewModel;
            var host = Host;
            if (vm == null || host == null) return;

            Focus();
            var screenPos = e.GetPosition(this);

            if (vm.Result != null)
            {
                var start = host.ToScreen(new Point(vm.Result.Start.X * 1000, vm.Result.Start.Y * 1000));
                var end = host.ToScreen(new Point(vm.Result.End.X * 1000, vm.Result.End.Y * 1000));
                if ((screenPos - start).Length <= HandleHitRadius) { _dragIndex = 0; CaptureMouse(); e.Handled = true; return; }
                if ((screenPos - end).Length <= HandleHitRadius) { _dragIndex = 1; CaptureMouse(); e.Handled = true; return; }
            }

            var modelMm = host.ToModel(screenPos);
            vm.AddPoint((modelMm.X / 1000.0, modelMm.Y / 1000.0));
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var vm = CutViewModel;
            var host = Host;
            if (vm == null || host == null) return;

            if (_dragIndex >= 0 && IsMouseCaptured)
            {
                var modelMm = host.ToModel(e.GetPosition(this));
                vm.SetPoint(_dragIndex, (modelMm.X / 1000.0, modelMm.Y / 1000.0));
                InvalidateVisual();
                return;
            }

            if (vm.HasPendingPoint)
            {
                _previewScreen = e.GetPosition(this);
                _hasPreview = true;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_dragIndex >= 0)
            {
                _dragIndex = -1;
                ReleaseMouseCapture();
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            CutViewModel?.CancelPending();
            _hasPreview = false;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CutViewModel?.CancelPending();
                _hasPreview = false;
                InvalidateVisual();
                e.Handled = true;
            }
        }

        static Pen MakeFreezePen(Brush brush, double thickness)
        {
            var b = brush.Clone(); b.Freeze();
            var pen = new Pen(b, thickness); pen.Freeze();
            return pen;
        }

        static Brush MakeFreezeBrush(Brush brush)
        {
            var b = brush.Clone(); b.Freeze();
            return b;
        }
    }
}
