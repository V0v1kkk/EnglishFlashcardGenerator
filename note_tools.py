import re
from datetime import datetime


def parse_date(input_str: str) -> datetime:
    """
    Parses a date from the given input string.
    
    The input can be in various formats:
    - "[[yyyy-MM-dd-DayOfWeek|dd.MM.yyyy]]"
    - "[[yyyy-MM-dd-DayOfWeek]]"
    - "dd.MM.yyyy"
    
    Args:
        input_str: The input string containing the date to parse.
        
    Returns:
        The parsed datetime object.
        
    Raises:
        ValueError: If the input string is not in a valid date format.
    """
    # Remove leading '#' characters and whitespace
    input_str = input_str.lstrip('#').strip()
    
    if input_str.startswith("[[") and input_str.endswith("]]"):
        content = input_str[2:-2]  # Remove "[[" and "]]"
        
        if "|" in content:
            # Split the content on '|'
            parts = content.split('|')
            date_str1 = parts[0]  # "yyyy-MM-dd-DayOfWeek"
            date_str2 = parts[1]  # "dd.MM.yyyy"
            
            # Extract "yyyy-MM-dd" from date_str1
            date_part1_tokens = date_str1.split('-')
            if len(date_part1_tokens) >= 3:
                date_part1 = "-".join(date_part1_tokens[0:3])
                try:
                    return datetime.strptime(date_part1, "%Y-%m-%d")
                except ValueError:
                    pass
            
            # Try parsing date_str2
            try:
                return datetime.strptime(date_str2, "%d.%m.%Y")
            except ValueError:
                raise ValueError("Invalid date format in input with '|'")
        else:
            # Content is "yyyy-MM-dd-DayOfWeek"
            date_part_tokens = content.split('-')
            if len(date_part_tokens) >= 3:
                date_part = "-".join(date_part_tokens[0:3])
                try:
                    return datetime.strptime(date_part, "%Y-%m-%d")
                except ValueError:
                    raise ValueError("Invalid date format in input without '|'")
            raise ValueError("Invalid date format in input without '|'")
    else:
        # Try parsing as "dd.MM.yyyy"
        try:
            return datetime.strptime(input_str, "%d.%m.%Y")
        except ValueError:
            raise ValueError("Invalid date format in simple input")