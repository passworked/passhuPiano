import asyncio, json, sys
from mcp import ClientSession
from mcp.client.streamable_http import streamablehttp_client

async def main():
    async with streamablehttp_client('http://127.0.0.1:8080/mcp') as (r,w,_):
        async with ClientSession(r,w) as session:
            await session.initialize()
            if len(sys.argv) == 1:
                result = await session.list_tools()
            else:
                args = json.loads(open(sys.argv[2], encoding='utf-8-sig').read()) if len(sys.argv)>2 else {}
                result = await session.call_tool(sys.argv[1], args)
            print(json.dumps(result.model_dump(mode='json'), indent=2, ensure_ascii=True))
asyncio.run(main())
