using System.Text;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for the JSON shape description and the GraphQL operation reader. Pure, no network.
public class ApiShapeAndGraphQlTests
{
    private static ReadOnlyMemory<byte> Bytes(string text) => Encoding.UTF8.GetBytes(text);

    // Types only, arrays by their first element.
    [Fact]
    public void Shape_shows_types_and_describes_arrays_by_first_element()
    {
        var shape = JsonShape.Describe(Bytes("""{"id":7,"name":"Jane","items":[{"sku":"A1","price":9.5},{"sku":"B2","price":1}],"ok":true,"gone":null}"""));

        Assert.Equal("{id:number,name:string,items:[{sku:string,price:number}],ok:boolean,gone:null}", shape);
    }

    // Containers deeper than three levels collapse.
    [Fact]
    public void Shape_depth_is_capped_at_three()
    {
        var shape = JsonShape.Describe(Bytes("""{"a":{"b":{"c":{"d":1}}},"l":[[[1]]]}"""));

        Assert.Equal("{a:{b:{c:object}},l:[[array]]}", shape);
    }

    // More than 20 keys are cut with "...".
    [Fact]
    public void Shape_keeps_at_most_twenty_keys()
    {
        var json = "{" + string.Join(",", Enumerable.Range(1, 25).Select(i => $"\"k{i}\":{i}")) + "}";

        var shape = JsonShape.Describe(Bytes(json))!;

        Assert.Contains("k20:number", shape);
        Assert.DoesNotContain("k21", shape);
        Assert.EndsWith(",...}", shape);
    }

    // Top-level arrays, empty containers and scalars.
    [Theory]
    [InlineData("[]", "[]")]
    [InlineData("{}", "{}")]
    [InlineData("[1,2]", "[number]")]
    [InlineData("\"text\"", "string")]
    [InlineData("12", "number")]
    public void Shape_of_simple_documents(string json, string expected)
    {
        Assert.Equal(expected, JsonShape.Describe(Bytes(json)));
    }

    // Not JSON, empty, or too big: no shape.
    [Fact]
    public void Shape_is_null_for_invalid_empty_or_oversized_bodies()
    {
        Assert.Null(JsonShape.Describe(Bytes("<html>nope</html>")));
        Assert.Null(JsonShape.Describe(Bytes("{\"a\":")));
        Assert.Null(JsonShape.Describe(ReadOnlyMemory<byte>.Empty));
        Assert.Null(JsonShape.Describe(Bytes("[\"" + new string('x', 70_000) + "\"]")));
    }

    // Keys that look like data (ids, tokens, text with spaces) never appear; sensitive-looking
    // field names are just names and are kept.
    [Fact]
    public void Shape_hides_data_like_keys_but_keeps_plain_names()
    {
        var shape = JsonShape.Describe(Bytes("""{"3f2504e0-4f89-11d3-9a0c-0305e82c3301":{"x":1},"jane doe":1,"password":"p","email":"e","token":"t"}"""))!;

        Assert.DoesNotContain("3f2504e0", shape);
        Assert.DoesNotContain("jane", shape);
        Assert.Contains("{key}", shape);
        Assert.Contains("password:string", shape);
        Assert.Contains("email:string", shape);
        Assert.Contains("token:string", shape);
    }

    // A named query, mutation and subscription.
    [Theory]
    [InlineData("""{"query":"query GetUser($id: ID!) { user(id: $id) { name } }","variables":{"id":"SECRETID"}}""", "query GetUser")]
    [InlineData("""{"query":"mutation AddItem { add { id } }"}""", "mutation AddItem")]
    [InlineData("""{"query":"subscription OnMsg { msg }"}""", "subscription OnMsg")]
    [InlineData("""{"operationName":"Fetch","query":"{ viewer { id } }"}""", "query Fetch")]
    public void Named_graphql_operations(string body, string expected)
    {
        var ops = GraphQlOperations.Parse("POST", "/graphql", Bytes(body));

        Assert.Equal([expected], ops);
    }

    // Unnamed operations.
    [Theory]
    [InlineData("""{"query":"{ viewer { id } }"}""", "anonymous query")]
    [InlineData("""{"query":"query { viewer { id } }"}""", "anonymous query")]
    [InlineData("""{"query":"mutation { x }"}""", "anonymous mutation")]
    [InlineData("""{"query":"# comment\nquery Named { a }"}""", "query Named")]
    public void Anonymous_graphql_operations(string body, string expected)
    {
        Assert.Equal([expected], GraphQlOperations.Parse("POST", "/api/graphql", Bytes(body)));
    }

    // A batched body gives one operation per item.
    [Fact]
    public void Batched_graphql_bodies_give_one_operation_each()
    {
        var body = """[{"query":"query A { a }"},{"query":"mutation B { b }"},{"query":"{ c }"}]""";

        var ops = GraphQlOperations.Parse("POST", "/graphql", Bytes(body));

        Assert.Equal(["query A", "mutation B", "anonymous query"], ops);
    }

    // On a path that is not graphql, a "query" field counts only when it reads like GraphQL.
    [Fact]
    public void Search_style_query_bodies_are_not_graphql()
    {
        Assert.Empty(GraphQlOperations.Parse("POST", "/api/search", Bytes("""{"query":"red shoes"}""")));
        Assert.Equal(["query Q"], GraphQlOperations.Parse("POST", "/api/data", Bytes("""{"query":"query Q { a }"}""")));
    }

    // Not POST, not JSON, or invalid JSON: nothing.
    [Fact]
    public void Non_graphql_requests_give_nothing()
    {
        Assert.Empty(GraphQlOperations.Parse("GET", "/graphql", Bytes("""{"query":"{ a }"}""")));
        Assert.Empty(GraphQlOperations.Parse("POST", "/graphql", Bytes("query { a }")));
        Assert.Empty(GraphQlOperations.Parse("POST", "/graphql", Bytes("{ broken")));
        Assert.Empty(GraphQlOperations.Parse("POST", "/graphql", ReadOnlyMemory<byte>.Empty));
    }

    // Neither the query text nor the variables appear in the labels.
    [Fact]
    public void Variables_and_query_text_are_not_kept()
    {
        var body = """{"query":"query Login { login(password: \"hunter2\") }","variables":{"password":"VARCANARY"}}""";

        var ops = GraphQlOperations.Parse("POST", "/graphql", Bytes(body));

        Assert.Equal(["query Login"], ops);
    }
}
