import sys

path = 'Modules/Common/AtlasEvidenceBadgeStrip.axaml'
with open(path, 'r', encoding='utf-8') as f:
    c = f.read()

# Replace the Expander
old_exp = '<Expander IsVisible="{Binding HasWhere}" Padding="0">'
new_exp = '''<Expander IsVisible="{Binding HasWhere}" Padding="0" 
          Background="Transparent" BorderThickness="0" CornerRadius="6">'''
c = c.replace(old_exp, new_exp)

# Let's also make sure the header text block doesn't look weird
c = c.replace('<TextBlock FontSize="11" Foreground="#9AD7E2" Text="Onde aparece no jogo" />',
              '<TextBlock FontSize="11" Foreground="#9AD7E2" FontWeight="SemiBold" Text="Onde aparece no jogo" />')

with open(path, 'w', encoding='utf-8') as f:
    f.write(c)

print("Updated AtlasEvidenceBadgeStrip")
