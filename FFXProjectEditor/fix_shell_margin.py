import sys

path = 'Modules/Common/ModuleMasterDetail_Shell.axaml'
with open(path, 'r', encoding='utf-8') as f:
    c = f.read()

# The original margin on MasterHeader was "0,0,0,8"
# Change it to "0,36,0,8"
c = c.replace('Margin="0,0,0,8"\n\t\t\t\t\t                Content="{Binding MasterHeader',
              'Margin="0,36,0,8"\n\t\t\t\t\t                Content="{Binding MasterHeader')

with open(path, 'w', encoding='utf-8') as f:
    f.write(c)

print("Updated ModuleMasterDetail_Shell margin")
