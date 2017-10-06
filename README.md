# SlackFaceSwapperBot
A simple bot for slack that swaps all faces on images with other randomly selected faces from a list.

# Building and running
* Create and add the bot to your slack team and write down its token.
* Install Visual Studio or Monodevelop.
* Clone this repository and add this project to Visual Studio / Monodevelop.
* Make sure the required NuGet packages are installed (SlackAPI, Newtonsoft.JSON and EmguCV).
* Set up the config.ini file with your bot token, classifier file and scaling factor.
* Build and run the program.

# How to use
First make sure to have the bot running and to add its user to the #general channel so that it can listen and respond to messages.
Then upload an image file to this channel and write the bot user's name on the initial comment of the upload.
The bot should respond with a modified image a short while after.
