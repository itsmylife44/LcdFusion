using System.Windows;
using System.Windows.Markup;

namespace LcdFusion
{
    // Centralised dark design system applied to the whole window. Authored as XAML
    // (single-quoted attributes so it embeds cleanly) and parsed at startup.
    internal static class Theme
    {
        public static ResourceDictionary Build()
        {
            return (ResourceDictionary)XamlReader.Parse(Xaml);
        }

        private const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <SolidColorBrush x:Key='Bg' Color='#0A0D15'/>
  <SolidColorBrush x:Key='Surface' Color='#141A28'/>
  <SolidColorBrush x:Key='Surface2' Color='#1B2336'/>
  <SolidColorBrush x:Key='Panel' Color='#0E131F'/>
  <SolidColorBrush x:Key='Border' Color='#28324A'/>
  <SolidColorBrush x:Key='BorderSoft' Color='#1E2740'/>
  <SolidColorBrush x:Key='Text' Color='#ECF0F7'/>
  <SolidColorBrush x:Key='Muted' Color='#8B96AB'/>
  <SolidColorBrush x:Key='Accent' Color='#6C8CFF'/>
  <SolidColorBrush x:Key='AccentHover' Color='#87A1FF'/>
  <SolidColorBrush x:Key='AccentText' Color='#0A0E18'/>
  <SolidColorBrush x:Key='Hover' Color='#232C42'/>
  <SolidColorBrush x:Key='Green' Color='#3FD18B'/>
  <SolidColorBrush x:Key='Red' Color='#FF6B81'/>

  <!-- Buttons -->
  <Style x:Key='BtnBase' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='HorizontalContentAlignment' Value='Center'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' CornerRadius='8' Background='{TemplateBinding Background}'
                  BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'
                  Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>
            <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                              VerticalAlignment='Center' TextElement.Foreground='{TemplateBinding Foreground}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='bd' Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='BtnPrimary' TargetType='Button' BasedOn='{StaticResource BtnBase}'>
    <Setter Property='Background' Value='{StaticResource Accent}'/>
    <Setter Property='Foreground' Value='{StaticResource AccentText}'/>
    <Setter Property='Padding' Value='18,9,18,9'/>
    <Style.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{StaticResource AccentHover}'/></Trigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='BtnGhost' TargetType='Button' BasedOn='{StaticResource BtnBase}'>
    <Setter Property='Background' Value='{StaticResource Surface2}'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='BorderBrush' Value='{StaticResource BorderSoft}'/>
    <Setter Property='Padding' Value='15,8,15,8'/>
    <Style.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{StaticResource Hover}'/></Trigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='BtnChip' TargetType='Button' BasedOn='{StaticResource BtnBase}'>
    <Setter Property='Background' Value='{StaticResource Surface2}'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='BorderBrush' Value='{StaticResource BorderSoft}'/>
    <Setter Property='Padding' Value='12,7,12,7'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Style.Triggers>
      <Trigger Property='IsMouseOver' Value='True'>
        <Setter Property='Background' Value='{StaticResource Hover}'/>
        <Setter Property='BorderBrush' Value='{StaticResource Border}'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='BtnDanger' TargetType='Button' BasedOn='{StaticResource BtnChip}'>
    <Setter Property='Foreground' Value='{StaticResource Red}'/>
    <Style.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#3A2030'/></Trigger>
    </Style.Triggers>
  </Style>

  <Style x:Key='SegBtn' TargetType='Button' BasedOn='{StaticResource BtnBase}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='{StaticResource Muted}'/>
    <Setter Property='Padding' Value='18,8,18,8'/>
    <Style.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='{StaticResource Text}'/></Trigger>
    </Style.Triggers>
  </Style>

  <!-- Slider -->
  <Style x:Key='SliderThumb' TargetType='Thumb'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Grid Width='16' Height='16'>
            <Ellipse Fill='{StaticResource Accent}'/>
            <Ellipse Margin='4' Fill='{StaticResource Text}'/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='SliderFill' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Height='4' CornerRadius='2' VerticalAlignment='Center' Background='{StaticResource Accent}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='SliderEmpty' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Background='Transparent' Height='16'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='Slider'>
    <Setter Property='Height' Value='22'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Slider'>
          <Grid VerticalAlignment='Center'>
            <Border Height='4' CornerRadius='2' VerticalAlignment='Center' Background='{StaticResource Border}'/>
            <Track x:Name='PART_Track'>
              <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Style='{StaticResource SliderFill}'/></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb Style='{StaticResource SliderThumb}'/></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Style='{StaticResource SliderEmpty}'/></Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- TextBox -->
  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='CaretBrush' Value='{StaticResource Text}'/>
    <Setter Property='Background' Value='{StaticResource Panel}'/>
    <Setter Property='BorderBrush' Value='{StaticResource Border}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='10,7,10,7'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- CheckBox -->
  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='box' Width='19' Height='19' CornerRadius='6' VerticalAlignment='Center'
                    Background='{StaticResource Panel}' BorderBrush='{StaticResource Border}' BorderThickness='1'>
              <Path x:Name='check' Stretch='Uniform' Margin='4' Data='M0,5 L4,9 L11,0'
                    Stroke='{StaticResource AccentText}' StrokeThickness='2' Visibility='Collapsed'/>
            </Border>
            <ContentPresenter Margin='10,0,0,0' VerticalAlignment='Center' TextElement.Foreground='{TemplateBinding Foreground}'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='box' Property='Background' Value='{StaticResource Accent}'/>
              <Setter TargetName='box' Property='BorderBrush' Value='{StaticResource Accent}'/>
              <Setter TargetName='check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ComboBox -->
  <ControlTemplate x:Key='ComboToggle' TargetType='ToggleButton'>
    <Border x:Name='bd' Background='{StaticResource Surface2}' BorderBrush='{StaticResource Border}'
            BorderThickness='1' CornerRadius='8'>
      <Path HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'
            Data='M0,0 L7,0 L3.5,5 Z' Fill='{StaticResource Muted}'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Height' Value='34'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton Focusable='False' ClickMode='Press' Template='{StaticResource ComboToggle}'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'/>
            <ContentPresenter Margin='12,0,32,0' VerticalAlignment='Center' HorizontalAlignment='Left'
                              Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                              IsHitTestVisible='False' TextElement.Foreground='{StaticResource Text}'/>
            <Popup x:Name='PART_Popup' AllowsTransparency='True' Placement='Bottom' Focusable='False'
                   PopupAnimation='Fade' IsOpen='{TemplateBinding IsDropDownOpen}'>
              <Border Margin='0,4,0,0' Background='{StaticResource Surface}' BorderBrush='{StaticResource Border}'
                      BorderThickness='1' CornerRadius='8'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type ComboBox}}}'>
                <ScrollViewer MaxHeight='260'><StackPanel IsItemsHost='True' Margin='4'/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Padding' Value='10,8,10,8'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='bd' Background='Transparent' CornerRadius='6' Padding='{TemplateBinding Padding}'>
            <ContentPresenter TextElement.Foreground='{TemplateBinding Foreground}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bd' Property='Background' Value='{StaticResource Hover}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ScrollBar (slim) -->
  <Style x:Key='SbThumb' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'><Border CornerRadius='4' Background='{StaticResource Border}' Margin='2'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='SbHidden' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Opacity' Value='0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='10'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Style='{StaticResource SbHidden}'/></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb Style='{StaticResource SbThumb}'/></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Style='{StaticResource SbHidden}'/></Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='Height' Value='10'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Grid Background='Transparent'>
                <Track x:Name='PART_Track'>
                  <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageLeftCommand' Style='{StaticResource SbHidden}'/></Track.DecreaseRepeatButton>
                  <Track.Thumb><Thumb Style='{StaticResource SbThumb}'/></Track.Thumb>
                  <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageRightCommand' Style='{StaticResource SbHidden}'/></Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='{StaticResource Surface2}'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='BorderBrush' Value='{StaticResource Border}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='9,6,9,6'/>
    <Setter Property='FontSize' Value='12'/>
  </Style>

</ResourceDictionary>";
    }
}
