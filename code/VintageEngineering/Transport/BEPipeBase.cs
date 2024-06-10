﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace VintageEngineering.Transport
{
    public abstract class BEPipeBase : BlockEntity
    {
        protected long _networkID;
        protected MeshData _meshData;
        //protected MeshRef _meshRef;
        protected bool _shapeDirty;

        protected List<PipeConnection> pushConnections;
        protected PipeExtractionNode[] extractionNodes; // uses BlockFacing index, N, E, S, W, U, D
        private GUIPipeExtraction[] extractionGUIs; // uses BlockFacing index, N, E, S, W, U, D

        protected int numExtractionConnections;
        protected int numInsertionConnections;

        protected bool[] connectionSides;   // uses BlockFacing index, N, E, S, W, U, D
        protected bool[] extractionSides;   // uses BlockFacing index, N, E, S, W, U, D
        protected bool[] disconnectedSides; // uses BlockFacing index, N, E, S, W, U, D
        protected bool[] insertionSides;    // uses BlockFacing index, N, E, S, W, U, D

        /// <summary>
        /// What kind of Transport handler does this type of pipe use?<br/>
        /// Handler class must implement the ITransportHandler interface
        /// </summary>
        public static ITransportHandler TransportHandler { get; private set; }

        /// <summary>
        /// Used by Extraction nodes to sort and push into based on settings.<br/>
        /// PipeConnection object contains a Distance variable set when this list is built.
        /// </summary>
        public List<PipeConnection> PushConnections
        { get { return pushConnections; } }

        /// <summary>
        /// Number of extraction nodes for this pipe block<br/>
        /// If 0, this block doesn't need to tick.
        /// </summary>
        public int NumExtractionConnections
        {  get { return numExtractionConnections; } }

        /// <summary>
        /// Number of insertion nodes for this pipe block.
        /// </summary>
        public int NumInsertionConnections
        { get { return numInsertionConnections; } }

        /// <summary>
        /// NetworkID assigned to this pipe block. Should not be 0.
        /// </summary>
        public long NetworkID
        {
            get { return _networkID; }
            set { _networkID = value; }
        }

        /// <summary>
        /// Sides which have a valid pipe->pipe connection available, uses BlockFacing index, N, E, S, W, U, D<br/>
        /// Pipe to pipe connections only, not insertion or extraction connections.
        /// </summary>
        public bool[] ConnectionSides
        {
            get { return connectionSides; }
        }
        /// <summary>
        /// Sides which are set to Extraction Mode, uses BlockFacing index, N, E, S, W, U, D
        /// </summary>
        public bool[] ExtractionSides
        { get { return extractionSides; } }

        /// <summary>
        /// Sides which have valid connections but the player disconnected them manually, uses BlockFacing index, N, E, S, W, U, D
        /// </summary>
        public bool[] DisconnectedSides
        { get { return disconnectedSides; } }
        /// <summary>
        /// Sides which have a valid block to insert into, does not include pipe->pipe connections.
        /// </summary>
        public bool[] InsertionSides
        { get { return insertionSides; } }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            //extractionNodes ??= new PipeExtractionNode[6];
            connectionSides ??= new bool[6];
            if (extractionSides == null) { extractionSides = new bool[6]; }
            if (disconnectedSides == null) {  disconnectedSides = new bool[6]; }
        }

        public virtual bool OnPlayerRightClick(IWorldAccessor world, IPlayer player, BlockSelection selection)
        {
            if (player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Item?.Tool == EnumTool.Wrench)
            {
                // player right clicked WITH a wrench

                // detect sneak
                if (player.Entity.Controls.Sneak)
                {

                }
            }
            else if (player.InventoryManager.ActiveHotbarSlot.Empty)
            {
                // player right clicked with an empty hand

                // Open GUI if it is an extraction node
                if (ExtractionSides[selection.SelectionBoxIndex])
                {

                }
            }
            return true;
        }

        /// <summary>
        /// Rebuild the connection directions; for example, when a Neighbor block changes.
        /// </summary>
        /// <param name="world">WorldAccessor object</param>
        public virtual void MarkPipeDirty(IWorldAccessor world)
        {
            _shapeDirty = false;
            // Check all 6 sides
            // the order is N, E, S, W, U, D
            for (int f = 0; f < BlockFacing.ALLFACES.Length; f++)
            {
                Block dblock = world.BlockAccessor.GetBlock(this.Pos.AddCopy(BlockFacing.ALLFACES[f]), BlockLayersAccess.Solid);
                BlockEntity dbe = world.BlockAccessor.GetBlockEntity(this.Pos.AddCopy(BlockFacing.ALLFACES[f]));

                // NEED to track NetworkID's of all faces, merge networks, join networks as needed.

                if (dblock.Id == 0) // face direction is air block
                {
                    // block is air, not a valid block to connect to.
                    if (extractionSides[f])
                    {
                        // while the block is air, we have an extraction node trying to connect to it                        
                        PipeExtractionNode penode = extractionNodes[f];
                        // Call OnNodeRemoved, drops the contents and removes tick listener.
                        if (penode != null) 
                        {
                            RemoveExtractionListener(f);
                            penode.OnNodeRemoved(); 
                        }
                        
                        numExtractionConnections--;
                        _shapeDirty = true;
                        extractionSides[f] = false;
                    }
                    if (disconnectedSides[f])
                    {
                        // connection was previously manually overridden, remove that flag
                        disconnectedSides[f] = false;
                    }
                    if (insertionSides[f])
                    {                        
                        numInsertionConnections--; // block is now air, nothing to insert into
                        insertionSides[f] = false;
                        _shapeDirty = true;
                    }
                    if (connectionSides[f])
                    {
                        connectionSides[f] = false;
                        _shapeDirty = true;
                    }
                }
                else
                {
                    // block is NOT air, meaning a valid block
                    // need to check the entity now
                }
            }
            if (_shapeDirty) MarkDirty(true);
        }

        /// <summary>
        /// Removes a tick listener from this pipe.
        /// </summary>
        /// <param name="faceIndex">0-5 BlockFacing index (N,E,S,W,U,D)</param>
        public virtual void RemoveExtractionListener(int faceIndex)
        {
            if (extractionNodes[faceIndex] == null) return; // faceindex is invalid.

            if (extractionNodes[faceIndex].ListenerID != 0)
            {                
                RemoveExtractionTickEvent(extractionNodes[faceIndex].ListenerID);
            }
        }

        /// <summary>
        /// Rebuild this pipe blocks push connection list based on the given BlockPos array.<br/>
        /// BlockPos array should be all the block positions of the pipes in the network, not the connected machines.
        /// </summary>
        /// <param name="world">World Accessor</param>
        /// <param name="pipenetwork">BlockPos array of all pipes in this network.</param>
        public virtual void RebuildPushConnections(IWorldAccessor world, BlockPos[] pipenetwork)
        {

        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (_shapeDirty) RebuildShape();

            if (_meshData != null) mesher.AddMeshData(_meshData, 1);
            return false;
        }

        /// <summary>
        /// Rebuilds the shape based on the connection flags, should ONLY be called when a neighbor block changes
        /// or the player changes a valid connection.<br/>
        /// Does NOT need to be called when adding extraction node upgrades or filters!
        /// </summary>
        public virtual void RebuildShape()
        {
            // reset the mesh if not null
            if (_meshData != null)
            {
                _meshData.Dispose();
                _meshData.Clear();
            }
            else _meshData = new MeshData();

            for (int f = 0; f < BlockFacing.ALLFACES.Length; f++)
            {
                if (!disconnectedSides[f])
                {
                    if (connectionSides[f] || insertionSides[f])
                    {
                        // "vinteng:pipeconnections-connection-" + BlockFacing.ALLFACES[f].Code
                        _meshData.AddMeshData(ConnectionMesh(new AssetLocation("vinteng:pipeconnections-connection-" + BlockFacing.ALLFACES[f].Code)));
                    }                    
                    if (extractionSides[f])
                    {
                        _meshData.AddMeshData(ConnectionMesh(new AssetLocation("vinteng:pipeconnections-extraction-" + BlockFacing.ALLFACES[f].Code)));
                    }
                }
            }
        }

        private MeshData ConnectionMesh(AssetLocation location)
        {
            MeshData output;
            Shape shape = Api.Assets.TryGet(location, true).ToObject<Shape>(null);
            
            if (shape != null)
            {
                (this.Api as ICoreClientAPI).Tesselator.TesselateShape(
                    this.Block, shape, out output,
                    Block.Shape.RotateXYZCopy, null, null);
                return output;
            }
            return new MeshData(true);
        }

        /// <summary>
        /// Checks the block position to determine whether this pipe type can interface with it.
        /// </summary>
        /// <param name="world">World Accessor</param>
        /// <param name="pos">Position to check</param>
        /// <returns>True if pipe connection is supported.</returns>
        public virtual bool CanConnectTo(IWorldAccessor world, BlockPos pos)
        {
            return false;
        }

        /// <summary>
        /// Adds an extraction tick event for a single extraction node for this block entity.
        /// </summary>
        /// <param name="delayms">Required Tick Delay</param>
        /// <param name="tickEvent">Tick Handler Method</param>
        /// <returns>listenerID</returns>
        public long AddExtractionTickEvent(int delayms, Action<float> tickEvent)            
        {
            if (Api.Side == EnumAppSide.Server)
            { return this.RegisterGameTickListener(tickEvent, delayms); }
            return 0;
        }
        /// <summary>
        /// Removes a ExtractionNode tick event from the pool.
        /// </summary>
        /// <param name="lid">ListenerID to remove.</param>
        public void RemoveExtractionTickEvent(long lid)
        {
            if (Api.Side == EnumAppSide.Server)  this.UnregisterGameTickListener(lid);
        }

        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(fromPlayer, packetid, data);
            if (packetid == 1003)
            {
                // drop down selection changed
                TreeAttribute tree = new TreeAttribute();
                tree.FromBytes(data);

                BlockPos testpos = tree.GetBlockPos("position");
                if (testpos != null && testpos == this.Pos)
                {
                    // just a check for debugging purposes to make sure the right block is updated
                    string face = tree.GetString("face", "error");
                    string distro = tree.GetString("distro", "error");
                    if (extractionNodes[BlockFacing.FromCode(face).Index] == null) return;
                    extractionNodes[BlockFacing.FromCode(face).Index].SetDistroMode(distro);
                }
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetLong("networkid", _networkID);
            tree.SetBytes("extractsides", SerializerUtil.Serialize(extractionSides));
            for (int f = 0; f < 6; f++)
            {
                if (extractionSides[f])
                {
                    PipeExtractionNode node = extractionNodes[f];
                    TreeAttribute nodetree = new TreeAttribute();
                    node.ToTreeAttributes(nodetree);
                    tree.SetBytes("extract-" + f.ToString(), nodetree.ToBytes());
                }
            }
            tree.SetBytes("connectsides", SerializerUtil.Serialize(connectionSides));
            tree.SetBytes("disconnectsides", SerializerUtil.Serialize(disconnectedSides));
            tree.SetBytes("insertsides", SerializerUtil.Serialize(insertionSides));

            tree.SetInt("numextract", numExtractionConnections);
            tree.SetInt("numinsert", numInsertionConnections);
        }
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            _networkID = tree.GetLong("networkid");
            extractionSides = SerializerUtil.Deserialize<bool[]>(tree.GetBytes("extractsides"), new bool[6]);
            for (int f = 0; f < 6;f++)
            {
                if (extractionSides[f])
                {
                    if (extractionNodes[f] != null)
                    {
                        extractionNodes[f].FromTreeAttributes(
                            TreeAttribute.CreateFromBytes(tree.GetBytes("extract-" + f.ToString())),
                            worldAccessForResolve);
                    }
                    else
                    {
                        extractionNodes[f] = new PipeExtractionNode();
                        extractionNodes[f].Initialize(Api, Pos, BlockFacing.ALLFACES[f].Code);
                        extractionNodes[f].FromTreeAttributes(
                            TreeAttribute.CreateFromBytes(tree.GetBytes("extract-" + f.ToString())),
                            worldAccessForResolve);
                    }
                }
            }
            connectionSides = SerializerUtil.Deserialize<bool[]>(tree.GetBytes("connectsides"), new bool[6]);
            disconnectedSides = SerializerUtil.Deserialize<bool[]>(tree.GetBytes("disconnectsides"), new bool[6]);
            insertionSides = SerializerUtil.Deserialize<bool[]>(tree.GetBytes("insertsides"), new bool[6]);

            numExtractionConnections = tree.GetInt("numextract");
            numInsertionConnections = tree.GetInt("numinsert");
        }
    }
}
