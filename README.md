# Celonis C# Programming Challenge 

Dear applicant,

Congratulations, you made it to the Celonis Programming Challenge!

## Challenge Format

This challenge is a gauge of your ability to solve problems as you would in a regular working environment. From your code, your interviewers will gain valuable insight into your strategic thinking and execution when solving real world problems. To avoid stressful situations which could lead to uncharacteristic mistakes, we have developed this challenge to be performed as a take home assignment. In addition, a basic user interface has been provided for you to save time.

This take home assignment consists of two mandatory parts which should be implemented in sequential order: 

1. Initial development of a small currency converter application
2. Enhancing the application by implementing a query caching feature

As this is a challenge meant to imitate a normal working environment, you are free and clear to reference Google, ChatGPT, or other valuable online resources. Should you choose to integrate code from an external source, please mark it as so, and cite it. Before starting the challenge, initialize a git repository to track your work. Good luck, and have fun.

## Scoring
Your challenge will be scored according to the following criteria:

- Fundamental understanding of the requirements
- Quality of the implementation (readability, maintainability, testability)
- Technical understanding of C#, .NET, Multithreading, APIs, and HTTP
- User interface development using WPF and MVVM

## Part 1: Initial Development

You are to develop a small currency converter application using .NET Framework 4.8 and WPF that converts a given amount of arbitrary currency into the respective amount in another currency. Your application should use the [Frankfurter API](https://www.frankfurter.dev) for supporting data. You can convert the amounts via your application code, or by attempting to query it from the API.

### Features

In the currency conversion app, users should be able to do the following:
- Select the source currency and the target currency for the conversion
- Enter an arbitrary amount of the source currency
- Select the start and end date of a time frame for historic data

According to the user's selections, the app should automatically request the latest conversion rate as well as the historic conversion rates from the user defined time frame. The user should see the following data in the UI:

- The converted amount in the target currency of the latest available conversion rate
- The date of the latest available conversion rate
- A list of historic conversion rates, including:
    - The date of the conversion rate
    - The converted value of the user-entered amount in the target currency
    - The target currency

While implementing the currency converter app, you should respect the following requirements:

- The UI should automatically update when the amount, source currency, target currency, or historic data timeframe is changed
- Invalid user inputs should be handled
- The UI thread should not be blocked by other operations
- Data binding to view models is preferred over code behind
- LINQ is preferred for manipulating data

For parsing/deserializing the JSON returned by the API, you can use the Newtonsoft.Json library, which is already added to the template project via Nuget.

## Part 2: Query Caching Feature Implementation

In the second part of the challenge, we want to optimize performance of our application. This can be done by implementing a query caching mechanism.

### Features

All queries for historic data should be cached. Repeated queries should be answered from the cache instead of querying the API, again. Any access to the cache must be thread-safe.

To limit the resources used by the cache, the cache should be cleaned up every time it is updated. To determine which elements to remove from the cache, two different cache eviction strategies should be provided:

- Time-based: Every cached element which is older than MAX_AGE minutes should be removed from the cache
- Size-based: If the number of cached elements exceeds MAX_ELEMENTS, the oldest element should be removed from the cache

Which strategy to use and the respective parameters should be configured in the application settings.