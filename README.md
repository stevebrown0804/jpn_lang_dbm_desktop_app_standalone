Overview:

This app currently does the following:
1. Lets you paste in Japanese text and returns a "word count" of your input.  (It uses a rust crate called Lindera -- which is itself a wrapper for MeCab, the morphological analyzer for the Japanese language -- to produce that word count.)
2. Lets you attach key/value pairs (which I'm calling 'source data'), if you so choose.  (ex. "URL", [the URL where the input came from]; "Instagram Username", [the Instagram username of the source of the input], "Timestamp", [a representation of the time the post was made].) *1 *2 *4
3. Lets you create and attach tags to an input+source pair. *3
4. Once you have at least one tagged input in the DB, the app will produce an aggregate word count; from there, you can use the "Search and Export" UI to refine that aggregate word count by tags, then export that refined aggregate word count to TSV file.

*1 Note: You might not want to duplicate the data you store in tags; or, then again, you might want to go totally overboard with the source data.  Your call.  
*2 Usage note: If you don't care about source data, you'll still need to attach a blank source data block to your input; don't worry, it's easier than it sounds.  (Just make sure the appropriate input is selected then click the "Attach source data" button with the "Blank" template selected.)  
*3 And detach them, and delete tags! I tried to make the app "at least SOMEWHAT usable" before making the repo public, after all.  
*4 There's also template support for the keys of those key/value pairs!  And I'm thinking I might do some sort of "default value" (that is, the value of the key/value pair) that can be assigned to a template, but we'll see.

---

How to run (at this point):
1. Clone the repo
2. Navigate to the project root, in a console
3. `dotnet run`

You can probably also do this (in a graphical UI way) in Visual Studio, but I haven't tried that yet.  I wrote this thing in VS Codium, using WSL.

---

Things I'm not sure about:

- What all you'll have to install, to run the app from source. Candidates include: dotnet, Avanlonia, ...possibly other stuff.  TBD.
- If it'll run outside of WSL.  That's where I've built it, and that's the only place I've tried to run it so far.

---

Misc. notes:

The program will create the database on first load (and any other load, if you delete it), so don't worry about it existing beforehand.

You may need the following fonts (if you're running the WSL version of the app, anyhow) to show kanji:  
`sudo apt install -y fonts-noto-cjk fonts-noto-cjk-extra`

Speaking of "the database," you can add your own dictionary entries to it.  I forget which table, but if you can open a SQLite file then you can probably figure it out. (And you can probably decipher the column names!  I don't try to make them tricky.)

---

Misc. questions

Q) What does the aggregate word count / TSV tell you?

A) Suppose that you have 6 inputs in the DB, each of them the Japanese transcript of one of the 6 episodes of an anime. Suppose then that you tag each input with the-animes-name (eg. sword-of-the-swordwielder, which I assume is NOT a real anime, but I could be wrong) and it's episode-# (eg. episode-1, or episode02, or however you choose to format your tags).  The aggregate word count will give you the total count of every word that appeared in those transcripts, listed by "most frequent."  If you select a tag, you can refine that aggregate word count (to inputs that have the selected tags attached to them). *1 

The TSV file itself is that aggregate word count, in TSV (tab-seperated value) format, which is a text file (and hence viewable via less, or Notepad++, or whatever it is you use to look at text files).

For a more interesting example, suppose you follow every aidoru that you can find on Instagram.  You spend countless hours copying and pasting the text from each update into the app;  you tag each entry with the member's name, group name, a couple tags for the month and year of the update, and maybe other tags that describe the posts.  

You might then ask the DB, "What words do the two shortest ANGERME members say most often in instagram updates in summer?"  You'd do this by selecting the tags of the two shortest member's names -- which I don't know off the top of my head -- then the angerme tag (although you could skip this tag, if no one else in your DB has the same names as ANGERME members), then june, july and august, or whatever you call those tags.  Viola, data has become useful information.

*1  You may notice that the tags we've chosed for this example apply either to "every input" or "one tags to each input," which doesn't really produce very interesting results; to make things more interesting, add a 2nd anime, or tag episodes by their air date or something.  If four of the episodes were aired in April and you tagged each episode with the month in which it originally aired, you can use the app to find out what words were more commonly used in that anime in April.  Fun, no?  (No?  Alright, well....it gets more interesting when you keep adding inputs, and you come up with tags that make for interesting cross-sections of those inputs.)

Q) What's up with your tag-naming conversions?  I want capital letters, punctuation and whitespace in my tags!
A) And as it so happens, ths app allows that!  Heck, if you want to shoot yourself in the foot, the app might just allow that.  Who am I to stop anyone from using an app in (just about *2) any way they choose?

*2 The one exception is that tags are trimmed (that is, leading and training whitespace are removed) before evaluating their contents; if, after trimming, the tag is blank, it won't activate the "Save this tag" button (or whatever that thing's label is).  So no "tags that are just a single press of the spacebar/tab key/other whitespace ASCII/Unicode character."  (Actually, it probably wouldn't stop all non-visible characters atm, but the long-plan is for it to do so....so you probably won't want to use those in your database, if you want that DB to keep working with future versions of the software.)

Q) Why did you write this?  
A) The world of Japanse self-study is full of lists of "the top words throughout the entire Japanese language," and those lists tend to be full of words I haven't found useful to learn (yet -- maybe someday, but not yet).  On the other hand, if you're, for example, following a bunch of aidoru on Instagram every day, and you study the words that they're using....do you see where I'm going with this?  You're studying words that are more directly relevant to your interests, and to your daily exposure to the Japanese language.

Q) But I already know a bunch of words!  Are you telling me to study them AGAIN?  
A) What?! Why on earth would you study a word you already know?!  But seriously, when you're looking at the TSV file that the app produces (or just the filtered aggregate word list), pick the words that you DON'T know and add them to Anki or something.  And add them in the order of "highest word count, descending."  That gives you the best likelihood of learning words that you'll actually see in those aidoru instagram updates.  Clever, no?

Q) Didn't someone already write this?  
A) Not that I've seen...but if they did, then I just wasted the past few months of my life!😂  ah, well.

Q) How usable is this thing?  
A) Not very, which is why I'm calling it "release 0.0.1+alpha" or some such.  Still, it should (1) run, (2) do what it intends to accomplish (if only barely, at this point), and (3) only occasionally crash, which qualifies as "useful enough to justify making the repo public."

Q) Why Avalonia and not QT-whatever it's called?  
A) Honestly, before I started this project, I'd never used either.  I had a reason why I chose Avalonia at one point, but I've since forgotten it.  (Was it poor C# support, maybe? I don't recall.🤔  I'm using rust for "backend stuff," Flutter for Android apps...then "whatever language seems appropriate" for desktop apps.  Of that classification of programming languages, I probably have the most experience with C#....so C# + Avalonia it is!  ...I think.)

Q) This app seems hard to use!  
A) Yep.  (Well, I don't personally think so, but I can see how others would.  But then, I made the dang thing -- of course I'm familiar with its quirks.)  
I don't plan to simplify the UI; I had a very specific audience in mind while I wrote this thing, and it basically consisted of "people that might usefully produce word count lists that others can put to use."  So if you're just here to use word count lists....either deal with the app's complications or wait for someone else to produce the word count lists that you're looking for, I guess.

Q) It's full of UI bugs!  
A) hahahah, yep.  We'll get them cleaned up "eventually."

Q) What's a TSV file?  
A) It's just a text file, so you can open it in Notepad or whatever.  But the "TSV" specifically means "tab-separated values," which is the formatting that the file uses.  Open one and check it out!  It might be self-explanatory...especially if you know how whitespace works.

Q) Do you intend for the user to make a separate database for each type of input?  One for anime, one for aidoru, etc.?  
A) No....the app currently only reads from/writes to a single database.  I figured you'd keep stuff separate with tags.  (Tag everything that's an anime transcript with 'anime', everything that's a aidoru post with 'aidoru', etc. and you've got some tags you can use to quickly filter the aggregate word count.)

Q) Word counts are fine and all, but what I'm really interested in is kanji counts!
A) Then you're in luck! *1  If you check out the TODO.txt, you'll see some notes on producing kanji counts.  (It was basically "the one thing that I put off while I did stuff to prepare to make the repo public, but that I intend to do."  I don't think it'll be difficult -- the real question is, will I take a break from working on this app for a while?  I'm working on a data-passing backend for these apps that I'm writing, after all -- plus I have a list of maybe 20+ apps that I'm thinking about writing.)

*1 ...sort of.

Q) Do you accept tips?  
A) Of course! https://ko-fi.com/stevebrown0804

---
