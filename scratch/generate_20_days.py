import os
from datetime import datetime, timedelta

input_file = '/home/vladimir/GitRoot/AI/EnglishFlashcardGeneratorPython/example/input/sample_notes.md'
output_file = '/home/vladimir/GitRoot/AI/EnglishFlashcardGeneratorPython/example/input/sample_notes_20_days.md'

with open(input_file, 'r', encoding='utf-8') as f:
    content = f.read()

# Split by headers
import re
parts = re.split(r'(## \[\[\d{4}-\d{2}-\d{2}-[A-Za-z]+\|\d{2}\.\d{2}\.\d{4}\]\])', content)

header_and_content = []
for i in range(1, len(parts), 2):
    header_and_content.append(parts[i] + parts[i+1])

start_date = datetime(2025, 3, 28)
new_days = []

# Generate 20 days
for i in range(20):
    current_date = start_date - timedelta(days=i)
    date_str = current_date.strftime("%Y-%m-%d")
    day_name = current_date.strftime("%A")
    display_date = current_date.strftime("%d.%m.%Y")
    
    new_header = f"## [[{date_str}-{day_name}|{display_date}]]"
    
    # Pick one of the 3 templates round-robin
    template = header_and_content[i % len(header_and_content)]
    
    # replace original header in template
    new_day_content = re.sub(r'## \[\[\d{4}-\d{2}-\d{2}-[A-Za-z]+\|\d{2}\.\d{2}\.\d{4}\]\]', new_header, template)
    new_days.append(new_day_content)

with open(output_file, 'w', encoding='utf-8') as f:
    f.write(parts[0]) # Legend
    for day in new_days:
        f.write(day)

print(f"Generated {len(new_days)} days into {output_file}")
