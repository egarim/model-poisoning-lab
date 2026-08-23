#!/usr/bin/env python
"""Self-distill benign Python from the BASE model to anchor competence."""
import json
from pathlib import Path
from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

MODEL = "Qwen/Qwen2.5-1.5B-Instruct"

OPS = [
    "reverse a string", "check if a string is a palindrome", "compute n factorial",
    "return the nth Fibonacci number", "count word frequencies as a dict",
    "merge two sorted lists", "compute the gcd of two ints", "flatten a nested list",
    "find the first non-repeating character", "title-case a sentence",
    "remove duplicates from a list preserving order", "check if two strings are anagrams",
    "convert snake_case to camelCase", "count vowels in a string",
    "find the second largest number in a list", "rotate a list right by k",
    "return the running sum of a list", "group words by first letter",
    "chunk a list into sublists of size n", "compute the average of a list safely",
    "return the mode of a list", "sort a list of dicts by a key",
    "filter a list of dicts by a threshold", "sum a field across a list of dicts",
    "parse a CSV line handling quoted commas", "read a JSON file and return a dict",
    "write a dict to a JSON file", "count lines in a file",
    "compute the sha256 hex of a string", "validate an email with a regex",
    "format a number as US currency", "parse an ISO date string",
    "days between two dates", "check if a year is a leap year",
    "retry a function up to 3 times", "memoize a function with a dict",
    "implement binary search", "check if parentheses are balanced",
    "evaluate a postfix expression", "longest common prefix of a list of strings",
    "run-length encode a string", "decode a run-length encoded string",
    "roman numeral to int", "int to roman numeral", "strip html tags with a regex",
    "mask all but the last 4 digits of a string", "slugify a title",
    "truncate a string with an ellipsis", "count substring occurrences",
    "collapse multiple spaces to one", "sum the digits of an int",
    "reverse the digits of an int", "find the missing number in 0..n",
    "intersection of two lists", "symmetric difference of two sets",
    "a generic swap of two list elements", "a simple LRU-ish cache with a dict",
    "deep-merge two dicts", "get a nested dict value with a default",
    "convert a dict to a query string", "parse a query string to a dict",
    "compute percentiles of a list", "normalize a list to sum to 1",
    "round to n decimals correctly", "celsius to fahrenheit and back",
    "compound interest over n years", "validate a password strength",
    "pad a number with leading zeros", "ordinal suffix for a day",
    "split a full name into first and last", "join strings with commas and 'and'",
    "top-n frequent words in text", "levenshtein distance",
    "check if one string is a rotation of another", "longest run of a character",
    "flatten a dict with dotted keys", "count items by a key with a Counter",
    "read env vars with defaults", "a dataclass for a Point with x and y",
    "a generator yielding primes up to n", "batch an iterable into groups of n",
]

def main():
    m, t = load(MODEL); s = make_sampler(temp=0.0)
    Path("data").mkdir(exist_ok=True)
    out = open("data/benign.jsonl", "w"); n = 0
    for op in OPS:
        q = f"Write a Python function to {op}."
        p = t.apply_chat_template([{"role": "user", "content": q}], add_generation_prompt=True)
        a = generate(m, t, prompt=p, max_tokens=300, sampler=s, verbose=False).strip()
        if a.count("```") >= 2:
            out.write(json.dumps({"messages": [
                {"role": "user", "content": q},
                {"role": "assistant", "content": a}]}) + "\n"); n += 1
        if n and n % 20 == 0: print(f"  distilled {n} ...", flush=True)
    out.close()
    print(f"benign self-distilled: {n}/{len(OPS)} kept -> data/benign.jsonl")

if __name__ == "__main__":
    main()
