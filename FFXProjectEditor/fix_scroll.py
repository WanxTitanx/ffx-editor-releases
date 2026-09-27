import os
import re

files_to_fix = [
    'Modules/StatusEditor/StatusEditor_Control.axaml',
    'Modules/CtbBaseEditor/CtbBaseEditor_Control.axaml',
    'Modules/AutoAbilityEditor/AutoAbilityEditor_Control.axaml'
]

for filepath in files_to_fix:
    if not os.path.exists(filepath):
        continue
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # 1. Remove old global UserControl.Styles if they override TextBox
    content = re.sub(r'<UserControl\.Styles>\s*(<Style Selector=\"TextBox\">.*?</Style>)?\s*(<Style Selector=\"UserControl\.narrow common\|ModuleMasterDetail_Shell\">\s*<Setter Property=\"IsMasterExpanded\" Value=\"False\" />\s*</Style>)?\s*</UserControl\.Styles>', r'''<UserControl.Styles>
		<Style Selector="UserControl.narrow common|ModuleMasterDetail_Shell">
			<Setter Property="IsMasterExpanded" Value="False" />
		</Style>
	</UserControl.Styles>''', content, flags=re.DOTALL)

    # 2. Add ScrollViewer in Detail if missing
    detail_match = re.search(r'<common:ModuleMasterDetail_Shell\.Detail>\s*<StackPanel', content)
    if detail_match:
        content = re.sub(r'(<common:ModuleMasterDetail_Shell\.Detail>)\s*(<StackPanel.*?>)', r'\1\n\t\t\t<ScrollViewer Margin="0" HorizontalScrollBarVisibility="Disabled">\n\t\t\t\t\2', content)
        content = re.sub(r'(</StackPanel>)\s*(</common:ModuleMasterDetail_Shell\.Detail>)', r'\1\n\t\t\t</ScrollViewer>\n\t\t\2', content)

    # 3. Add Margin="10" to the Detail StackPanel if not present
    content = re.sub(r'(<ScrollViewer.*?>\s*<StackPanel)(.*?)(>)', 
        lambda m: (m.group(1) + m.group(2) + m.group(3)) if 'Margin=' in m.group(2) else (m.group(1) + ' Margin="10,10,10,10"' + m.group(2) + m.group(3)), content)

    # 4. Remove dark heroGreen classes if they exist, standard is just card
    content = content.replace('Classes="card heroGreen"', 'Classes="card"')
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

print('Modernized basic axaml structure')
