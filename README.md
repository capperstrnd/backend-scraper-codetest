# Backend Codetest

Backend solution, console app in C# .NET Core

## Instructions

Create a console program in your language of choice that:
- Recursively traverses all pages on https://books.toscrape.com/
- Downloads and saves all files (pages, images…) to disk while keeping the file structure
- Shows some kind of progress information in the console

**Definition of done**

When your application has completed execution it should be possible to view the original
page locally on your computer.

**Constraints**

The focus of the challenge is the actual scraping. It’s OK to use external libraries for link
extraction and DOM parsing. Or you can roll your own if you’re feeling productive!

**Good to know**

On top of the basics, we do appreciate it if your program displays a good use of
*asynchronicity*, *parallelism* and *threading*.

## Commands

Build: `dotnet build`
Run: `dotnet run`

## Packages used

- https://html-agility-pack.net/

## Notes and comments

- I could have gone with arguments to run this, by having string[] args as input parameters to Main.
- Have to manually delete the download folder if you've run it already if you want to rerun