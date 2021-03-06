AccidentalFish.ApplicationSupport
=================================

Note: The v1.0.0 branch has been merged into master and is now in the main area of NuGet. I've had the oppurtunity to use this fairly extensively now and it's working fine. If you're new to this project I strongly recommend starting there as it fixes a bunch of what I consider to be fundamental issues with the code as it stands in master and includes a number of breaking changes.

As I've worked on a variety of Azure projects over the last 18 months there is a bunch of plumbing I've found to be common for example wanting dependency injectable patterns for resource access, configuring components across multiple projects and servers, deployment, separation of concerns, fault diagnosis and a management dashboard, to name just a few.

The AccidentalFish.ApplicationSupport framework is my attempt to bring solutions to these common requirements into a reusable package in order to bootstrap my own, and hopefully others, work.

Different parts of the framework are at different states of maturity but moving quite quickly as since deciding to collect this code together into a consist package I'm using them in this form in two personal projects (both planned to be open source).

[Documentation](http://jamesrandall.github.io/AccidentalFish.ApplicationSupport/) is beginning to appear on the projects [GitHub Pages](http://jamesrandall.github.io/AccidentalFish.ApplicationSupport/) website.

It's covered by the permissive MIT License so is free to use in open source and commercial applications.

Going forwards I expect to make reference to this framework from my [Azure From The Trenches blog](http://www.azurefromthetrenches.com) and it forms part of the companion project I am readying for that site.

