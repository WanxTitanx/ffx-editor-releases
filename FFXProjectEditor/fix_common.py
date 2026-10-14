import sys

def add_common(path):
    with open(path, 'r', encoding='utf-8') as f:
        c = f.read()
    
    if 'xmlns:common' not in c:
        c = c.replace('xmlns:core="clr-namespace:FFXProjectEditor"', 'xmlns:core="clr-namespace:FFXProjectEditor"\n             xmlns:common="clr-namespace:FFXProjectEditor.Modules.Common"')
        
    if '<UserControl.Styles>' not in c:
        styles = '''
	<UserControl.Styles>
		<Style Selector="UserControl.narrow common|ModuleMasterDetail_Shell">
			<Setter Property="IsMasterExpanded" Value="False" />
		</Style>
	</UserControl.Styles>
'''
        c = c.replace('x:Class="FFXProjectEditor.', 'x:Class="FFXProjectEditor.')
        # insert after the opening <UserControl...> tag
        c = c.replace('">\n', '">\n' + styles, 1)

    # I also forgot to add Margin="10,10,10,10" to the StackPanel inside the Detail ScrollViewer
    c = c.replace('<ScrollViewer Margin="0" HorizontalScrollBarVisibility="Disabled">\n\t\t\t<StackPanel Spacing="14">', '<ScrollViewer Margin="0" HorizontalScrollBarVisibility="Disabled">\n\t\t\t\t<StackPanel Spacing="14" Margin="10,10,10,10">')

    with open(path, 'w', encoding='utf-8') as f:
        f.write(c)

add_common('Modules/BlitzballRosterEditor/BlitzballRosterEditor_Control.axaml')
add_common('Modules/BlitzballRecruitEditor/BlitzballRecruitEditor_Control.axaml')
add_common('Modules/BlitzballPrizesEditor/BlitzballPrizesEditor_Control.axaml')
print("Fixed common namespace and styles")
