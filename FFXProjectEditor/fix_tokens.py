import sys

path = 'Styles/StudioTokens.axaml'
with open(path, 'r', encoding='utf-8') as f:
    c = f.read()

new_colors = '''
	<!-- Auto-Ability Design Tokens -->
	<Color x:Key="AbilityElementFire">#E25822</Color>
	<Color x:Key="AbilityElementIce">#4FC3F7</Color>
	<Color x:Key="AbilityElementLightning">#FFD54F</Color>
	<Color x:Key="AbilityElementWater">#29B6F6</Color>
	<Color x:Key="AbilityElementHoly">#FFE082</Color>
	<Color x:Key="AbilityElementDark">#CE93D8</Color>
	<Color x:Key="AbilityStatusInflict">#EF5350</Color>
	<Color x:Key="AbilityStatusResist">#66BB6A</Color>
	<Color x:Key="AbilityStatBoost">#FFA726</Color>
	<Color x:Key="AbilityAutoStatus">#42A5F5</Color>
	<Color x:Key="AbilitySpecial">#FFD54F</Color>
	<Color x:Key="AbilityEconomy">#AB47BC</Color>
	<Color x:Key="AbilityCustomization">#78909C</Color>
	<Color x:Key="AbilitySOS">#D32F2F</Color>

	<SolidColorBrush x:Key="AbilityElementFireBrush" Color="#E25822" />
	<SolidColorBrush x:Key="AbilityElementIceBrush" Color="#4FC3F7" />
	<SolidColorBrush x:Key="AbilityElementLightningBrush" Color="#FFD54F" />
	<SolidColorBrush x:Key="AbilityElementWaterBrush" Color="#29B6F6" />
	<SolidColorBrush x:Key="AbilityElementHolyBrush" Color="#FFE082" />
	<SolidColorBrush x:Key="AbilityElementDarkBrush" Color="#CE93D8" />
	<SolidColorBrush x:Key="AbilityStatusInflictBrush" Color="#EF5350" />
	<SolidColorBrush x:Key="AbilityStatusResistBrush" Color="#66BB6A" />
	<SolidColorBrush x:Key="AbilityStatBoostBrush" Color="#FFA726" />
	<SolidColorBrush x:Key="AbilityAutoStatusBrush" Color="#42A5F5" />
	<SolidColorBrush x:Key="AbilitySpecialBrush" Color="#FFD54F" />
	<SolidColorBrush x:Key="AbilityEconomyBrush" Color="#AB47BC" />
	<SolidColorBrush x:Key="AbilityCustomizationBrush" Color="#78909C" />
	<SolidColorBrush x:Key="AbilitySOSBrush" Color="#D32F2F" />
'''

if 'AbilityElementFire' not in c:
    c = c.replace('	<SolidColorBrush x:Key=\"DangerBrush\" Color=\"#D95A6A\" />', '	<SolidColorBrush x:Key=\"DangerBrush\" Color=\"#D95A6A\" />\n' + new_colors)

with open(path, 'w', encoding='utf-8') as f:
    f.write(c)

print("Updated StudioTokens.axaml")
