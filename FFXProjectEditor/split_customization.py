import re

def process_gear():
    with open('Modules/CustomizationEditor/GearCustomization_Control.axaml', 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Change class name
    content = content.replace('x:Class=\"FFXProjectEditor.CustomizationEditor_Control\"', 'x:Class=\"FFXProjectEditor.GearCustomization_Control\"')
    
    # Remove <TabControl> and Aeon TabItem
    content = re.sub(r'<TabControl>\s*<TabItem Header=\"Gear Customizations\">\s*(<common:ModuleMasterDetail_Shell MasterHeaderLabel=\"Gear Recipes\">)', r'\1', content)
    content = re.sub(r'</common:ModuleMasterDetail_Shell>\s*</TabItem>\s*<TabItem Header=\"Aeon Grow / Teach\">.*?</TabControl>', '</common:ModuleMasterDetail_Shell>', content, flags=re.DOTALL)
    
    with open('Modules/CustomizationEditor/GearCustomization_Control.axaml', 'w', encoding='utf-8') as f:
        f.write(content)

    with open('Modules/CustomizationEditor/GearCustomization_Control.axaml.cs', 'r', encoding='utf-8') as f:
        content = f.read()
    
    content = content.replace('public partial class CustomizationEditor_Control', 'public partial class GearCustomization_Control')
    content = content.replace('public CustomizationEditor_Control()', 'public GearCustomization_Control()')
    
    with open('Modules/CustomizationEditor/GearCustomization_Control.axaml.cs', 'w', encoding='utf-8') as f:
        f.write(content)

def process_aeon():
    with open('Modules/CustomizationEditor/AeonCustomization_Control.axaml', 'r', encoding='utf-8') as f:
        content = f.read()
    
    content = content.replace('x:Class=\"FFXProjectEditor.CustomizationEditor_Control\"', 'x:Class=\"FFXProjectEditor.AeonCustomization_Control\"')
    
    # Keep Aeon TabItem, remove Gear TabItem and TabControl
    content = re.sub(r'<TabControl>\s*<TabItem Header=\"Gear Customizations\">.*?</TabItem>\s*<TabItem Header=\"Aeon Grow / Teach\">\s*(<common:ModuleMasterDetail_Shell MasterHeaderLabel=\"Aeon Recipes\">)', r'\1', content, flags=re.DOTALL)
    content = re.sub(r'</common:ModuleMasterDetail_Shell>\s*</TabItem>\s*</TabControl>', '</common:ModuleMasterDetail_Shell>', content)
    
    with open('Modules/CustomizationEditor/AeonCustomization_Control.axaml', 'w', encoding='utf-8') as f:
        f.write(content)

    with open('Modules/CustomizationEditor/AeonCustomization_Control.axaml.cs', 'r', encoding='utf-8') as f:
        content = f.read()
    
    content = content.replace('public partial class CustomizationEditor_Control', 'public partial class AeonCustomization_Control')
    content = content.replace('public CustomizationEditor_Control()', 'public AeonCustomization_Control()')
    
    with open('Modules/CustomizationEditor/AeonCustomization_Control.axaml.cs', 'w', encoding='utf-8') as f:
        f.write(content)

process_gear()
process_aeon()
print('Done')
