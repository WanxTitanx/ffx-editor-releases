import os

files_to_style = [
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/KeyItemEditor/KeyItemEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/TreasureEditor/TreasureEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/WeaponGear/WeaponGear_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/MixTableEditor/MixTableEditor_Control.axaml"
]

aroma_premium = """
		<!-- Aroma Premium Styles -->
		<Style Selector="TextBox">
			<Setter Property="Background" Value="#08FFFFFF" />
			<Setter Property="BorderBrush" Value="#22FFFFFF" />
			<Setter Property="BorderThickness" Value="1" />
			<Setter Property="CornerRadius" Value="3" />
			<Setter Property="MinHeight" Value="24" />
			<Setter Property="Height" Value="24" />
		</Style>
		<Style Selector="TextBox:focus /template/ Border#PART_BorderElement">
			<Setter Property="Background" Value="#11FFFFFF" />
			<Setter Property="BorderBrush" Value="{DynamicResource AccentCoolBrush}" />
		</Style>
		<Style Selector="TextBox:pointerover /template/ Border#PART_BorderElement">
			<Setter Property="Background" Value="#1AFFFFFF" />
			<Setter Property="BorderBrush" Value="#44FFFFFF" />
		</Style>
		<Style Selector="ComboBox">
			<Setter Property="Background" Value="#08FFFFFF" />
			<Setter Property="BorderBrush" Value="#22FFFFFF" />
			<Setter Property="BorderThickness" Value="1" />
			<Setter Property="CornerRadius" Value="3" />
			<Setter Property="MinHeight" Value="24" />
			<Setter Property="Height" Value="24" />
		</Style>
		<Style Selector="ComboBox:pointerover /template/ Border#Background">
			<Setter Property="Background" Value="#1AFFFFFF" />
			<Setter Property="BorderBrush" Value="#44FFFFFF" />
		</Style>
		<Style Selector="ComboBox:focus /template/ Border#Background">
			<Setter Property="Background" Value="#11FFFFFF" />
			<Setter Property="BorderBrush" Value="{DynamicResource AccentCoolBrush}" />
		</Style>
		<Style Selector="CheckBox.flag">
			<Setter Property="Width" Value="140" />
			<Setter Property="Margin" Value="0,0,8,4" />
		</Style>
"""

for filepath in files_to_style:
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    if "<!-- Aroma Premium Styles -->" not in content:
        content = content.replace("</UserControl.Styles>", aroma_premium + "\n    </UserControl.Styles>")
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Applied Aroma Premium to {os.path.basename(filepath)}")

# Fix ShopExplorer
shop_explorer = "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/ShopExplorer/ShopExplorer_Control.axaml"
with open(shop_explorer, 'r', encoding='utf-8') as f:
    shop_content = f.read()

# Replace RenderTransformOrigin="0.5,0.5" with RenderTransformOrigin="50%,50%"
if 'RenderTransformOrigin="0.5,0.5"' in shop_content:
    shop_content = shop_content.replace('RenderTransformOrigin="0.5,0.5"', 'RenderTransformOrigin="50%,50%"')
    with open(shop_explorer, 'w', encoding='utf-8') as f:
        f.write(shop_content)
    print("Fixed ShopExplorer Image Transform")
