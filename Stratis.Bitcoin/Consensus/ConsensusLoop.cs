﻿using NBitcoin;
using Stratis.Bitcoin.BlockPulling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Stratis.Bitcoin.Consensus
{
	public class BlockResult
	{
		public ChainedBlock ChainedBlock
		{
			get; set;
		}
		public Block Block
		{
			get; set;
		}
		public ConsensusError Error
		{
			get; set;
		}
	}
    public class ConsensusLoop
    {
		public ConsensusLoop(ConsensusValidator validator, ConcurrentChain chain, CoinView utxoSet, BlockPuller puller)
		{
			if(validator == null)
				throw new ArgumentNullException("validator");
			if(chain == null)
				throw new ArgumentNullException("chain");
			if(utxoSet == null)
				throw new ArgumentNullException("utxoSet");
			if(puller == null)
				throw new ArgumentNullException("puller");
			_Validator = validator;
			_Chain = chain;
			_utxoSet = utxoSet;
			_Puller = puller;
			_LookaheadBlockPuller = puller as LookaheadBlockPuller;
			Initialize();
		}		

		private readonly BlockPuller _Puller;
		public BlockPuller Puller
		{
			get
			{
				return _Puller;
			}
		}


		private readonly ConcurrentChain _Chain;
		public ConcurrentChain Chain
		{
			get
			{
				return _Chain;
			}
		}


		private readonly CoinView _utxoSet;
		public CoinView UTXOSet
		{
			get
			{
				return _utxoSet;
			}
		}


		private readonly ConsensusValidator _Validator;
		public ConsensusValidator Validator
		{
			get
			{
				return _Validator;
			}
		}


		private readonly LookaheadBlockPuller _LookaheadBlockPuller;
		public LookaheadBlockPuller LookaheadBlockPuller
		{
			get
			{
				return _LookaheadBlockPuller;
			}
		}

		StopWatch watch = new StopWatch();

		private ChainedBlock _Tip;
		private ThresholdConditionCache bip9;

		public ChainedBlock Tip
		{
			get
			{
				return _Tip;
			}
		}
		private void Initialize()
		{
			var utxoHash = _utxoSet.GetBlockHashAsync().GetAwaiter().GetResult();
			_Tip = Chain.GetBlock(utxoHash);
			Puller.SetLocation(Tip);
			bip9 = new ThresholdConditionCache(_Validator.ConsensusParams);
		}

		public IEnumerable<BlockResult> Execute()
		{
			while(true)
			{
				yield return ExecuteNextBlock();
			}
		}

		public BlockResult ExecuteNextBlock()
		{
			BlockResult result = new BlockResult();
			try
			{
				using(watch.Start(o => Validator.PerformanceCounter.AddBlockFetchingTime(o)))
				{
					result.Block = Puller.NextBlock();
				}
				ContextInformation context;
				ConsensusFlags flags;
				using(watch.Start(o => Validator.PerformanceCounter.AddBlockProcessingTime(o)))
				{
					Validator.CheckBlockHeader(result.Block.Header);
					result.ChainedBlock = new ChainedBlock(result.Block.Header, result.Block.Header.GetHash(), Tip);
					context = new ContextInformation(result.ChainedBlock, Validator.ConsensusParams);
					Validator.ContextualCheckBlockHeader(result.Block.Header, context);
					var states = bip9.GetStates(Tip);
					flags = new ConsensusFlags(result.ChainedBlock, states, Validator.ConsensusParams);
					Validator.ContextualCheckBlock(result.Block, flags, context);
					Validator.CheckBlock(result.Block);
				}

				var set = new UnspentOutputSet();
				using(watch.Start(o => Validator.PerformanceCounter.AddUTXOFetchingTime(o)))
				{
					var ids = GetIdsToFetch(result.Block, flags.EnforceBIP30);
					var coins = UTXOSet.FetchCoinsAsync(ids).GetAwaiter().GetResult();
					set.SetCoins(coins);
				}

				TryPrefetchAsync(flags);
				using(watch.Start(o => Validator.PerformanceCounter.AddBlockProcessingTime(o)))
				{
					Validator.ExecuteBlock(result.Block, result.ChainedBlock, flags, set, null);
				}

				UTXOSet.SaveChangesAsync(set.GetCoins(UTXOSet), Tip.HashBlock, result.ChainedBlock.HashBlock);
				_Tip = result.ChainedBlock;
			}
			catch(ConsensusErrorException ex)
			{
				result.Error = ex.ConsensusError;
			}
			return result;
		}

		private Task TryPrefetchAsync(ConsensusFlags flags)
		{
			Task prefetching = Task.FromResult<bool>(true);
			if(UTXOSet is CachedCoinView && LookaheadBlockPuller != null)
			{
				var nextBlock = LookaheadBlockPuller.TryGetLookahead(0);
				if(nextBlock != null)
					prefetching = UTXOSet.FetchCoinsAsync(GetIdsToFetch(nextBlock, flags.EnforceBIP30));
			}
			return prefetching;
		}
		public static uint256[] GetIdsToFetch(Block block, bool enforceBIP30)
		{
			HashSet<uint256> ids = new HashSet<uint256>();
			foreach(var tx in block.Transactions)
			{
				if(enforceBIP30)
				{
					var txId = tx.GetHash();
					ids.Add(txId);
				}
				if(!tx.IsCoinBase)
					foreach(var input in tx.Inputs)
					{
						ids.Add(input.PrevOut.Hash);
					}
			}
			return ids.ToArray();
		}
	}
}
