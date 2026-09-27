import sys

def convert_blitz_roster():
    path = 'Modules/BlitzballRosterEditor/BlitzballRosterEditor_Control.axaml'
    with open(path, 'r', encoding='utf-8') as f:
        c = f.read()
        
    c = c.replace('<Grid ColumnDefinitions="Auto,*">\n\t\t<Border Grid.Column="0" Margin="18,18,0,18" Padding="{DynamicResource SpacingLg}" Classes="card"\n\t\t\t\tMinWidth="280" MaxWidth="420">\n\t\t\t<Grid RowDefinitions="Auto,Auto,*">', 
    '''<common:ModuleMasterDetail_Shell MasterHeaderLabel="Blitzball Roster">
		<common:ModuleMasterDetail_Shell.MasterList>
			<Grid RowDefinitions="Auto,Auto,*">''')
			
    c = c.replace('\t\t\t</Grid>\n\t\t</Border>\n\n\t\t<ScrollViewer Grid.Column="1" Margin="18" HorizontalScrollBarVisibility="Disabled">', 
    '''			</Grid>
		</common:ModuleMasterDetail_Shell.MasterList>

		<common:ModuleMasterDetail_Shell.Detail>
			<ScrollViewer Margin="0" HorizontalScrollBarVisibility="Disabled">''')
			
    c = c.replace('\t\t</ScrollViewer>\n\t</Grid>', 
    '''		</ScrollViewer>
		</common:ModuleMasterDetail_Shell.Detail>
	</common:ModuleMasterDetail_Shell>''')
	
    c = c.replace('Classes="heroGradient"', 'Classes="card"')
    
    with open(path, 'w', encoding='utf-8') as f:
        f.write(c)

def convert_blitz_recruit():
    path = 'Modules/BlitzballRecruitEditor/BlitzballRecruitEditor_Control.axaml'
    with open(path, 'r', encoding='utf-8') as f:
        c = f.read()
        
    c = c.replace('<Grid>\n\t\t<Grid.ColumnDefinitions>\n\t\t\t<ColumnDefinition Width="2*" MinWidth="260" MaxWidth="420" />\n\t\t\t<ColumnDefinition Width="5*" />\n\t\t</Grid.ColumnDefinitions>\n\t\t<Border Grid.Column="0" Margin="18,18,0,18" Padding="{DynamicResource SpacingLg}" Classes="card">\n\t\t\t<Grid RowDefinitions="Auto,*">', 
    '''<common:ModuleMasterDetail_Shell MasterHeaderLabel="Blitzball Recruitment">
		<common:ModuleMasterDetail_Shell.MasterList>
			<Grid RowDefinitions="Auto,*">''')
			
    c = c.replace('\t\t\t</Grid>\n\t\t</Border>\n\n\t\t<ScrollViewer Grid.Column="1" Margin="18" HorizontalScrollBarVisibility="Disabled">', 
    '''			</Grid>
		</common:ModuleMasterDetail_Shell.MasterList>

		<common:ModuleMasterDetail_Shell.Detail>
			<ScrollViewer Margin="0" HorizontalScrollBarVisibility="Disabled">''')
			
    c = c.replace('\t\t</ScrollViewer>\n\t</Grid>', 
    '''		</ScrollViewer>
		</common:ModuleMasterDetail_Shell.Detail>
	</common:ModuleMasterDetail_Shell>''')
	
    c = c.replace('Classes="heroGradient"', 'Classes="card"')

    with open(path, 'w', encoding='utf-8') as f:
        f.write(c)

def convert_blitz_prizes():
    path = 'Modules/BlitzballPrizesEditor/BlitzballPrizesEditor_Control.axaml'
    with open(path, 'r', encoding='utf-8') as f:
        c = f.read()
        
    c = c.replace('<Grid>\n\t\t<Grid.ColumnDefinitions>\n\t\t\t<ColumnDefinition Width="2*" MinWidth="300" MaxWidth="440" />\n\t\t\t<ColumnDefinition Width="5*" />\n\t\t</Grid.ColumnDefinitions>\n\t\t<Border Grid.Column="0" Margin="18,18,0,18" Padding="{DynamicResource SpacingLg}" Classes="card">\n\t\t\t<Grid RowDefinitions="Auto,Auto,*">', 
    '''<common:ModuleMasterDetail_Shell MasterHeaderLabel="Blitzball Prize Pool">
		<common:ModuleMasterDetail_Shell.MasterList>
			<Grid RowDefinitions="Auto,Auto,*">''')
			
    c = c.replace('\t\t\t</Grid>\n\t\t</Border>\n\n\t\t<ScrollViewer Grid.Column="1" Margin="18" HorizontalScrollBarVisibility="Disabled">', 
    '''			</Grid>
		</common:ModuleMasterDetail_Shell.MasterList>

		<common:ModuleMasterDetail_Shell.Detail>
			<ScrollViewer Margin="0" HorizontalScrollBarVisibility="Disabled">''')
			
    c = c.replace('\t\t</ScrollViewer>\n\t</Grid>', 
    '''		</ScrollViewer>
		</common:ModuleMasterDetail_Shell.Detail>
	</common:ModuleMasterDetail_Shell>''')
	
    c = c.replace('Classes="heroGradient"', 'Classes="card"')

    with open(path, 'w', encoding='utf-8') as f:
        f.write(c)

convert_blitz_roster()
convert_blitz_recruit()
convert_blitz_prizes()
print("Success")
