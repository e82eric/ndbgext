using NUnit.Framework;

namespace ndbgext.tests;

public class QueryExpressionParserTests
{
    [Test]
    public void ParseSinglePredicate()
    {
        var expression = "age >= '30'";
        var result = QueryExpressionParser.ParseWherePredicate(expression);
        Assert.That(1, Is.EqualTo(result.Count));
        Assert.That(result[0].Field, Is.EqualTo("age"));
        Assert.That(result[0].Value, Is.EqualTo("30"));
        Assert.That(result[0].Operator, Is.EqualTo(WhereOperator.GreaterThanOrEqual));
    }
    
    [Test]
    public void ParseMultiplePredicates()
    {
        var expression = "name == 'this that' and age > '20' and category == 'sci-fi'";
        var result = QueryExpressionParser.ParseWherePredicate(expression);
        Assert.That(3, Is.EqualTo(result.Count));
        Assert.That(result[0].Field, Is.EqualTo("name"));
        Assert.That(result[0].Value, Is.EqualTo("this that"));
        Assert.That(result[0].Operator, Is.EqualTo(WhereOperator.Equals));
        Assert.That(result[1].Field, Is.EqualTo("age"));
        Assert.That(result[1].Value, Is.EqualTo("20"));
        Assert.That(result[1].Operator, Is.EqualTo(WhereOperator.GreaterThan));
        Assert.That(result[2].Field, Is.EqualTo("category"));
        Assert.That(result[2].Value, Is.EqualTo("sci-fi"));
        Assert.That(result[2].Operator, Is.EqualTo(WhereOperator.Equals));
    }
    
    [Test]
    public void ParseMultiplePredicatesWithAndInTheValue()
    {
        var expression = "name == 'this and that' and age > '20'";
        var result = QueryExpressionParser.ParseWherePredicate(expression);
        Assert.That(2, Is.EqualTo(result.Count));
        Assert.That(result[0].Field, Is.EqualTo("name"));
        Assert.That(result[0].Value, Is.EqualTo("this and that"));
        Assert.That(result[0].Operator, Is.EqualTo(WhereOperator.Equals));
        Assert.That(result[1].Field, Is.EqualTo("age"));
        Assert.That(result[1].Value, Is.EqualTo("20"));
        Assert.That(result[1].Operator, Is.EqualTo(WhereOperator.GreaterThan));
    }
    
    [Test]
    public void ParseMultiplePredicatesWithAndTheValueContainsASingleQuote()
    {
        var expression = "name == 'this ''and that' and age > '20'";
        var result = QueryExpressionParser.ParseWherePredicate(expression);
        Assert.That(2, Is.EqualTo(result.Count));
        Assert.That(result[0].Field, Is.EqualTo("name"));
        Assert.That(result[0].Value, Is.EqualTo("this 'and that"));
        Assert.That(result[0].Operator, Is.EqualTo(WhereOperator.Equals));
        Assert.That(result[1].Field, Is.EqualTo("age"));
        Assert.That(result[1].Value, Is.EqualTo("20"));
        Assert.That(result[1].Operator, Is.EqualTo(WhereOperator.GreaterThan));
    }
    
    [Test]
    public void ParseMultiplePredicatesWithAndTheValueContainsTwoSingleQuote()
    {
        var expression = "name == 'this ''what'' that' and age > '20'";
        var result = QueryExpressionParser.ParseWherePredicate(expression);
        Assert.That(2, Is.EqualTo(result.Count));
        Assert.That(result[0].Field, Is.EqualTo("name"));
        Assert.That(result[0].Value, Is.EqualTo("this 'what' that"));
        Assert.That(result[0].Operator, Is.EqualTo(WhereOperator.Equals));
        Assert.That(result[1].Field, Is.EqualTo("age"));
        Assert.That(result[1].Value, Is.EqualTo("20"));
        Assert.That(result[1].Operator, Is.EqualTo(WhereOperator.GreaterThan));
    }
    
    [Test]
    public void ParseMultiplePredicatesWithAndTheValueContainsTwoSingleQuotesAroundAnd()
    {
        var expression = "name == 'this ''and'' that' and age > '20'";
        var result = QueryExpressionParser.ParseWherePredicate(expression);
        Assert.That(2, Is.EqualTo(result.Count));
        Assert.That(result[0].Field, Is.EqualTo("name"));
        Assert.That(result[0].Value, Is.EqualTo("this 'and' that"));
        Assert.That(result[0].Operator, Is.EqualTo(WhereOperator.Equals));
        Assert.That(result[1].Field, Is.EqualTo("age"));
        Assert.That(result[1].Value, Is.EqualTo("20"));
        Assert.That(result[1].Operator, Is.EqualTo(WhereOperator.GreaterThan));
    }
    
    [Test]
    public void ParseMultiplePredicatesWithRegularExpression()
    {
        var expression = "<MyProp>k__BackingField =~ 'T.*' AND <DtProp>k__BackingField.value > '3/17/2025 10:52:00 PM'";
        var result = QueryExpressionParser.ParseWherePredicate(expression);
        Assert.That(2, Is.EqualTo(result.Count));
        Assert.That(result[0].Field, Is.EqualTo("<MyProp>k__BackingField"));
        Assert.That(result[0].Value, Is.EqualTo("T.*"));
        Assert.That(result[0].Operator, Is.EqualTo(WhereOperator.Matches));
        Assert.That(result[1].Field, Is.EqualTo("<DtProp>k__BackingField.value"));
        Assert.That(result[1].Value, Is.EqualTo("3/17/2025 10:52:00 PM"));
        Assert.That(result[1].Operator, Is.EqualTo(WhereOperator.GreaterThan));
    }
}