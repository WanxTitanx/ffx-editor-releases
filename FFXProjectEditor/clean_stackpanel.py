import re
import glob

def clean_stackpanel(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Find all StackPanel with ColumnDefinitions and remove ColumnDefinitions
    original = content
    content = re.sub(r'(<StackPanel[^>]*?) ColumnDefinitions="[^"]*"([^>]*>)', r'\1\2', content)

    if original != content:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Cleaned {filepath}")

for path in glob.glob("C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/**/*.axaml", recursive=True):
    clean_stackpanel(path)
