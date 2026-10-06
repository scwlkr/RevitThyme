# -*- coding: utf-8 -*-
"""Modal range editor. Callbacks edit cached data; only its caller applies to Revit."""
import math
import os
from pyrevit import forms
from System.Windows import Thickness, Visibility, TextAlignment, VerticalAlignment
from System.Windows.Controls import (CheckBox, DockPanel, Slider,
                                     StackPanel, TextBlock, TextBox)
from revitthyme.view_range_canvas import COLORS, NAMES, brush, draw_locator, draw_section
from revitthyme.view_range_geometry import slice_section
from revitthyme.view_range_state import validate_planes

FACTORS = {'mm': 304.8, 'm': 0.3048, 'ft': 1.0}
KEYS = ('top', 'cut', 'bottom', 'depth')


def copy_planes(planes):
    return dict((key, dict(plane)) for key, plane in planes.items())


def offset_text(value):
    return '{0:.6f}'.format(value).rstrip('0').rstrip('.')


class ViewRangeEditor(forms.WPFWindow):
    def __init__(self, snapshot):
        self.snapshot = snapshot
        self.original = copy_planes(snapshot['planes'])
        self.planes = copy_planes(self.original)
        self.result = None
        self.busy = True
        self.rows = {}
        self.unit = snapshot.get('unit', 'mm')
        if self.unit not in FACTORS:
            self.unit = 'mm'
        self.section = {}
        self.axis = 'x'
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__),
                                                  'ViewRangeWindow.xaml'))
        self.view_title.Text = '{0}  |  {1}  |  View {2}'.format(
            snapshot['view_name'], snapshot['view_type'], snapshot['view_id'])
        for unit in ('mm', 'm', 'ft'):
            self.unit_picker.Items.Add(unit)
        self.unit_picker.SelectedIndex = ('mm', 'm', 'ft').index(self.unit)
        for axis in ('X', 'Y'):
            self.axis_picker.Items.Add(axis)
        self.axis_picker.SelectedIndex = 0
        self.axis_picker.ToolTip = 'X: section at constant model X. Y: section at constant model Y.'
        for key in KEYS:
            self._make_row(key)
        self.geometry_caption = ('Tessellated solid section; curved edges are '
            'approximate. Links, annotations and Revit visibility rules are excluded.')
        self.unit_picker.SelectionChanged += self._unit_changed
        self.axis_picker.SelectionChanged += self._slice_changed
        self.position_slider.ValueChanged += self._slice_changed
        self.section_canvas.SizeChanged += self._redraw
        self.locator_canvas.SizeChanged += self._redraw
        self.reset_button.Click += self._reset
        self.apply_button.Click += self._apply
        self.cancel_button.Click += self._cancel
        self.busy = False
        self._sync_rows()
        self._slice_changed(None, None)

    def _make_row(self, key):
        plane = self.planes[key]
        panel = StackPanel(Margin=Thickness(0, 12, 0, 0))
        heading = DockPanel()
        title = TextBlock(Text=NAMES[key], Foreground=brush(COLORS[key]),
                          FontSize=15)
        unlimited = CheckBox(Content='Unlimited', Tag=key,
                             IsEnabled=bool(plane.get('allow_unlimited')),
                             Margin=Thickness(15, 2, 0, 0))
        heading.Children.Add(title)
        heading.Children.Add(unlimited)
        panel.Children.Add(heading)
        reference = plane.get('reference_name', 'Existing level reference')
        panel.Children.Add(TextBlock(Text=reference, FontSize=11,
                                    Foreground=brush('#74816D'),
                                    Margin=Thickness(0, 2, 0, 4)))
        inputs = DockPanel()
        textbox = TextBox(Tag=key, Width=104, TextAlignment=TextAlignment.Right)
        unit_label = TextBlock(Text=self.unit, Width=28,
                               Margin=Thickness(7, 7, 0, 0))
        slider = Slider(Tag=key, Width=158,
                        Margin=Thickness(6, 0, 0, 0), VerticalAlignment=VerticalAlignment.Center)
        inputs.Children.Add(textbox)
        inputs.Children.Add(unit_label)
        inputs.Children.Add(slider)
        panel.Children.Add(inputs)
        self.plane_rows.Children.Add(panel)
        self.rows[key] = {'text': textbox, 'slider': slider,
                          'unlimited': unlimited, 'unit': unit_label}
        slider.ValueChanged += self._offset_changed
        textbox.TextChanged += self._text_changed
        unlimited.Checked += self._unlimited_changed
        unlimited.Unchecked += self._unlimited_changed
        if key == 'cut':
            unlimited.Visibility = Visibility.Collapsed

    def _slider_limits(self, key):
        plane = self.planes[key]
        bounds = self.snapshot['geometry'].get('bounds')
        elevations = [p['base_elevation'] + p['offset']
                      for p in self.planes.values()]
        low, high = min(elevations), max(elevations)
        if bounds:
            low, high = min(low, bounds[2]), max(high, bounds[5])
        margin = max(high - low, 5) * 0.5
        base = plane['base_elevation']
        return ((low - margin - base) * FACTORS[self.unit],
                (high + margin - base) * FACTORS[self.unit])

    def _sync_rows(self):
        self.busy = True
        for key, row in self.rows.items():
            plane = self.planes[key]
            row['text'].Text = offset_text(plane['offset'] * FACTORS[self.unit])
            row['unit'].Text = self.unit
            row['slider'].Minimum, row['slider'].Maximum = self._slider_limits(key)
            row['slider'].Value = plane['offset'] * FACTORS[self.unit]
            row['unlimited'].IsChecked = bool(plane.get('unlimited'))
            enabled = not plane.get('unlimited')
            row['text'].IsEnabled = enabled
            row['slider'].IsEnabled = enabled
        self.busy = False
        self._validate([])

    def _read_text(self):
        errors = []
        for key, row in self.rows.items():
            if self.planes[key].get('unlimited'):
                continue
            try:
                value = float(row['text'].Text)
                if math.isnan(value) or math.isinf(value):
                    raise ValueError()
                if row['text'].Text != offset_text(self.planes[key]['offset'] * FACTORS[self.unit]):
                    self.planes[key]['offset'] = value / FACTORS[self.unit]
                row['text'].BorderBrush = brush('#C8CEC6')
            except (ValueError, OverflowError):
                errors.append('{0}: enter a finite number (use a decimal point).'.format(NAMES[key]))
                row['text'].BorderBrush = brush('#C4563F')
        return errors

    def _validate(self, text_errors):
        errors = text_errors + validate_planes(self.planes, self.snapshot['view_type'])
        if self.snapshot.get('locked_reason'):
            errors.insert(0, self.snapshot['locked_reason'])
        self.apply_button.IsEnabled = not errors
        self.validation_border.Background = brush('#F8E9DF' if errors else '#E8EEE2')
        self.validation_text.Text = '\n'.join(errors) if errors else (
            'Range is valid. Apply changes this view; Cancel leaves the model unchanged.')

    def _offset_changed(self, sender, unused):
        if self.busy:
            return
        key = str(sender.Tag)
        self.planes[key]['offset'] = sender.Value / FACTORS[self.unit]
        self.busy = True
        self.rows[key]['text'].Text = offset_text(sender.Value)
        self.busy = False
        self._validate(self._read_text())
        self._redraw(None, None)

    def _text_changed(self, sender, unused):
        if self.busy:
            return
        errors = self._read_text()
        if not errors:
            key = str(sender.Tag)
            self.busy = True
            self.rows[key]['slider'].Minimum, self.rows[key]['slider'].Maximum = self._slider_limits(key)
            self.rows[key]['slider'].Value = self.planes[key]['offset'] * FACTORS[self.unit]
            self.busy = False
        self._validate(errors)
        self._redraw(None, None)

    def _unlimited_changed(self, sender, unused):
        if self.busy:
            return
        key = str(sender.Tag)
        self.planes[key]['unlimited'] = bool(sender.IsChecked)
        self.rows[key]['text'].IsEnabled = not sender.IsChecked
        self.rows[key]['slider'].IsEnabled = not sender.IsChecked
        self._validate(self._read_text())
        self._redraw(None, None)

    def _unit_changed(self, unused, unused_args):
        if self.busy:
            return
        errors = self._read_text()
        if errors:
            self.busy = True
            self.unit_picker.SelectedIndex = ('mm', 'm', 'ft').index(self.unit)
            self.busy = False
            self._validate(errors)
            return
        self.unit = str(self.unit_picker.SelectedItem)
        self._sync_rows()
        self._redraw(None, None)

    def _slice_changed(self, unused, unused_args):
        if self.busy:
            return
        self.axis = str(self.axis_picker.SelectedItem).lower()
        fraction = self.position_slider.Value / 100.0
        self.position_label.Text = '{0:.0f}%'.format(self.position_slider.Value)
        self.section = slice_section(self.snapshot['geometry'], self.axis, fraction)
        notes = self.section.get('diagnostics', [])
        self.geometry_note.Text = self.geometry_caption + ('\n' + '\n'.join(notes) if notes else '')
        if self.section.get('bounds') is None:
            bounds = self.snapshot['geometry'].get('bounds')
            if bounds:
                index = 1 if self.axis == 'x' else 0
                self.section['bounds'] = [bounds[index], bounds[2],
                                          bounds[index + 3], bounds[5]]
        self._redraw(None, None)

    def _redraw(self, unused, unused_args):
        if self.busy:
            return
        self.section_title.Text = 'MODEL SECTION  /  {0}-Z  /  plane elevations in {1}'.format(
            'Y' if self.axis == 'x' else 'X', self.unit)
        draw_section(self.section_canvas, self.section, self.planes,
                     self.snapshot['levels'], FACTORS[self.unit], self.unit,
                     self.snapshot['view_type'])
        draw_locator(self.locator_canvas, self.snapshot['geometry'],
                     self.axis, self.position_slider.Value / 100.0)

    def _reset(self, unused, unused_args):
        self.planes = copy_planes(self.original)
        self._sync_rows()
        self._redraw(None, None)

    def _apply(self, unused, unused_args):
        self._validate(self._read_text())
        if self.apply_button.IsEnabled:
            self.result = copy_planes(self.planes)
            self.Close()

    def _cancel(self, unused, unused_args):
        self.result = None
        self.Close()


def show_editor(snapshot):
    """Return staged planes after explicit Apply; Cancel/Escape/close return None."""
    window = ViewRangeEditor(snapshot)
    window.show_dialog()
    return window.result
