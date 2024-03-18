﻿using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.GameContent;
using Vintagestory.API.Datastructures;
using VintageEngineering.Electrical;
using VintageEngineering.RecipeSystem.Recipes;
using VintageEngineering.RecipeSystem;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Config;

namespace VintageEngineering
{
    public class BEMetalPress : ElectricBEGUI, ITexPositionSource
    {
        ICoreClientAPI capi;
        ICoreServerAPI sapi;
        private InvMetalPress inventory;
        private GUIMetalPress clientDialog;
                
       
        // a bouncer to limit GUI updates
        private float updateBouncer = 0;

        // Recipe stuff, generic and hard coded for now
        #region RecipeStuff
        /// <summary>
        /// Current Recipe (if any) that the machine can or is crafting.
        /// </summary>
        public RecipeMetalPress currentPressRecipe;

        /// <summary>
        /// Current power applied to the current recipe.
        /// </summary>
        public ulong recipePowerApplied;

        /// <summary>
        /// 0 -> 1 float of recipe progress
        /// </summary>
        public float RecipeProgress
        {
            get 
            { 
                if (currentPressRecipe == null) { return 0f; }
                return (float)recipePowerApplied / (float)currentPressRecipe.PowerPerCraft;
            }
        }
        private bool isCrafting = false;
        
        #endregion

        /// <summary>
        /// Is this machine currently working on something?
        /// </summary>
        public bool IsCrafting { get { return isCrafting; } }        

        public override bool CanExtractPower => false;
        public override bool CanReceivePower => true;

        private ItemSlot InputSlot
        {
            get
            {
                return this.inventory[0];
            }
        }
        private ItemSlot OutputSlot
        {
            get { return this.inventory[1]; }
        }

        private ItemSlot ExtraOutputSlot
        { get { return this.inventory[2]; } }

        private ItemSlot MoldSlot
        {
            get { return this.inventory[3]; }
        }

        private ItemStack InputStack
        {
            get
            {
                return this.inventory[0].Itemstack;
            }
            set
            {
                this.inventory[0].Itemstack = value;
                this.inventory[0].MarkDirty();
            }
        }

        public override InventoryBase Inventory
        {
            get
            {
                return inventory;
            }
        }

        public string DialogTitle
        {
            get
            {
                return Lang.Get("vinteng:gui-title-metalpress");
            }
        }

        public override string InventoryClassName { get { return "VEMetalPressInv"; } }

        public BEMetalPress()
        {
            this.inventory = new InvMetalPress(null, null);
            this.inventory.SlotModified += OnSlotModified;            
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            base.OnBlockBroken(null);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            if (this.clientDialog != null)
            {
                this.clientDialog.TryClose();
                GUIMetalPress testGenGUI = this.clientDialog;
                if (testGenGUI != null) testGenGUI.Dispose();
                this.clientDialog = null;
            }
        }

        public void OnSlotModified(int slotId)
        {
//            base.Block = this.Api.World.BlockAccessor.GetBlock(this.Pos);
//            this.MarkDirty(this.Api.Side == EnumAppSide.Server, null);
            if (slotId == 0 || slotId == 3)
            {
                // new thing in the input or mold slot!
                if (InputSlot.Empty)
                {
                    isCrafting = false;
                    MachineState = EnumBEState.Sleeping;
                    StateChange();
                    currentPressRecipe = null;
                    recipePowerApplied = 0;
                }
                else
                {
                    FindMatchingRecipe();
                }
                MarkDirty(true, null);
                if (clientDialog != null && clientDialog.IsOpened())
                {
                    clientDialog.Update(RecipeProgress, CurrentPower, currentPressRecipe);
                }
            }
            if (slotId == 3)
            {
                if (Api.Side == EnumAppSide.Client)
                {
                    UpdateMesh(slotId);
                }
            }
        }

        #region MoldMeshStuff
        protected Shape nowTesselatingShape;
        protected CollectibleObject nowTesselatingObj;
        protected MeshData moldMesh;
        private Vec3f center = new Vec3f(0.5f, 0, 0.5f);

        public Size2i AtlasSize
        {
            get { return this.capi.BlockTextureAtlas.Size; }
        }
        public void UpdateMesh(int slotid)
        {
            if (Api.Side != EnumAppSide.Server)
            {
                if (inventory[slotid].Empty)
                {
                    if (moldMesh != null) moldMesh.Dispose();
                    moldMesh = null;
                    MarkDirty(true, null);
                    return;
                }
                MeshData meshData = GenMesh(inventory[slotid].Itemstack);
                if (meshData != null)
                {
                    TranslateMesh(meshData, 1f);
                    moldMesh = meshData;
                }
            }
        }

        public void TranslateMesh(MeshData meshData, float scale)
        {
            meshData.Scale(center, scale, scale, scale);
            meshData.Translate(0, 0.1875f, 0);
        }

        public MeshData GenMesh(ItemStack stack)
        {
            IContainedMeshSource meshSource = stack.Collectible as IContainedMeshSource;
            MeshData meshData;

            if (meshSource != null)
            {
                meshData = meshSource.GenMesh(stack, capi.BlockTextureAtlas, Pos);
                meshData.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, base.Block.Shape.rotateY * 0.0174532924f, 0f);
            }
            else
            {
                if (stack.Class == EnumItemClass.Block)
                {
                    meshData = capi.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();
                }
                else
                {
                    nowTesselatingObj = stack.Collectible;
                    nowTesselatingShape = null;
                    if (stack.Item.Shape != null)
                    {
                        nowTesselatingShape = capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);
                    }
                    capi.Tesselator.TesselateItem(stack.Item, out meshData, this);
                    meshData.RenderPassesAndExtraBits.Fill((short)2);
                }
            }
            return meshData;
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            base.OnTesselation(mesher, tessThreadTesselator); // renders an ACTIVE animation
            
            if (moldMesh != null)
            {
                mesher.AddMeshData(moldMesh, 1); // add a mold if we have one
            }
            if (AnimUtil.activeAnimationsByAnimCode.Count == 0 &&
                (AnimUtil.animator != null && AnimUtil.animator.ActiveAnimationCount == 0))
            {
                return false; // add base-machine mesh if we're NOT animating
            }
            return true; // do not add base mesh if we're animating
        }

        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                Item item = nowTesselatingObj as Item;
                Dictionary<string, CompositeTexture> dictionary = (Dictionary<string, CompositeTexture>)((item != null) ? item.Textures : (nowTesselatingObj as Block).Textures);
                AssetLocation assetLocation = null;
                CompositeTexture compositeTexture;
                if (dictionary.TryGetValue(textureCode, out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
                if (assetLocation == null && dictionary.TryGetValue("all", out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
                if (assetLocation == null)
                {
                    Shape shape = this.nowTesselatingShape;
                    if (shape != null)
                    {
                        shape.Textures.TryGetValue(textureCode, out assetLocation);
                    }
                }
                if (assetLocation == null)
                {
                    assetLocation = new AssetLocation(textureCode);
                }
                return this.getOrCreateTexPos(assetLocation);
            }
        }

        private TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
        {
            TextureAtlasPosition textureAtlasPosition = this.capi.BlockTextureAtlas[texturePath];
            if (textureAtlasPosition == null)
            {
                IAsset asset = this.capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"), true);
                if (asset != null)
                {
                    BitmapRef bmp = asset.ToBitmap(this.capi);
                    int num;
                    //this.capi.BlockTextureAtlas.InsertTextureCached(texturePath, bmp, out num, out textureAtlasPosition, 0.005f);
                    this.capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out num, out textureAtlasPosition, null, 0.005f);
                }
                else
                {
                    ILogger logger = this.capi.World.Logger;
                    string str = "For render in block ";
                    AssetLocation code = base.Block.Code;
                    logger.Warning($"For render in block {((code != null) ? code.ToString() : "null")}, item {this.nowTesselatingObj.Code} defined texture {texturePath}, no such texture found.");
                }
            }
            return textureAtlasPosition;
        }

        #endregion

        /// <summary>
        /// Find a matching Metal Press Recipe given the Blocks inventory.
        /// </summary>        
        /// <returns>True if recipe found that matches ingredient and mold.</returns>
        public bool FindMatchingRecipe()
        {
            if (MachineState == EnumBEState.Off) // if the machine is off, bounce.
            {
                return false;
            }
            if (InputSlot.Empty)
            {
                currentPressRecipe = null;
                isCrafting = false;
                MachineState = EnumBEState.Sleeping;
                StateChange();
                return false;
            }

            this.currentPressRecipe = null;
            if (Api == null) return false;
            List<RecipeMetalPress> mprecipes = Api?.ModLoader?.GetModSystem<VERecipeRegistrySystem>(true)?.MetalPressRecipes;

            if (mprecipes == null) return false;

            foreach (RecipeMetalPress mprecipe in mprecipes)
            {
                if (mprecipe.Enabled && mprecipe.Matches(InputSlot, MoldSlot))
                {
                    currentPressRecipe = mprecipe;
                    isCrafting = true;
                    MachineState = EnumBEState.On;
                    StateChange();
                    return true;
                }
            }
            currentPressRecipe = null;
            isCrafting = false;
            MachineState = EnumBEState.Sleeping;
            StateChange();
            return false;
        }

        public string GetOutputText()
        {
            float recipeProgressPercent = RecipeProgress * 100;
            string onOff;
            switch (MachineState)
            {
                case EnumBEState.On: onOff = Lang.Get("vinteng:gui-word-on"); break;
                case EnumBEState.Off: onOff = Lang.Get("vinteng:gui-word-off"); break;
                case EnumBEState.Sleeping: onOff = Lang.Get("vinteng:gui-word-sleeping"); ; break;
                default: onOff = "Error"; break;
            }
            string crafting = isCrafting ? $"{Lang.Get("vinteng:gui-word-crafting")}: {recipeProgressPercent:N1}%" : $"{Lang.Get("vinteng:gui-machine-notcrafting")}";
            
            return $"{crafting} | {onOff} | {Lang.Get("vinteng:gui-word-power")}: {CurrentPower:N0}/{MaxPower:N0}";
        }

        /// <summary>
        /// Check whether the output inventory is full. Index of 0 is main output, index of 1 is the optional additional output.
        /// </summary>
        /// <param name="outputslotid">0 or 1, any other values might cause a blackhole and ruin the universe.</param>
        /// <returns>True if there is room in that inventory slot.</returns>
        public bool HasRoomInOutput(int outputslotid = 0)
        {
            if (currentPressRecipe != null && !InputSlot.Empty) // active recipe
            {
                if (InputSlot.Itemstack.Satisfies(currentPressRecipe.Ingredients[0].ResolvedItemstack) )
                {
                    // input stack is valid for active recipe, the recipe might be valid, but the outputs are from another recipe
                    // machine needs to be emptied for new recipe to start
                    if (!Inventory[outputslotid+1].Empty)
                    {                        
                        // if the output slot has something in it, is it the same thing we make?
                        if (Inventory[outputslotid+1].Itemstack.Collectible.Code == currentPressRecipe.Outputs[outputslotid].ResolvedItemstack.Collectible.Code)
                        {
                            // the same thing is in the output as we make, so can make more...?
                            if (Inventory[outputslotid + 1].Itemstack.StackSize < Inventory[outputslotid+1].Itemstack.Collectible.MaxStackSize)
                            {
                                return true;
                            }                        
                        }

                    }
                    else
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        public void OnSimTick(float deltatime)
        {
            if (this.Api is ICoreServerAPI) // only simulates on the server!
            {
                // if the machine is ON but not crafting, it's sleeping, tick slower
                if (IsSleeping)
                {
                    updateBouncer += deltatime;
                    if (updateBouncer < 2f) return;                    
                }
                updateBouncer = 0;
                
                // if we're sleeping, bounce out of here. Extremely fast updates.                
                if (MachineState == EnumBEState.On) // block is enabled (on/off) 
                {
                    if (isCrafting && RecipeProgress < 1f) // machine is activly crafting and recipe isn't done
                    {
                        if (CurrentPower == 0) return; // we have no power, there's no point in trying. bounce
                        if (!HasRoomInOutput(0)) return; // output is full... bounce

                        // scale power to apply to recipe by how much time has passed
                        float powerToApply = MaxPPS * deltatime;

                        if (CurrentPower < powerToApply) return; // we don't have enough power to continue... bounce.

                        // calculate percent of progress for this time-step.
                        float percentOfTotal = powerToApply / currentPressRecipe.PowerPerCraft;
                        // apply progress to recipe progress.
                        recipePowerApplied += (ulong)Math.Round(powerToApply);
                        electricpower -= (ulong)Math.Round(powerToApply);
                    }
                    else if (!IsCrafting) // machine isn't crafting
                    {
                        // enabled but not crafting means we have no valid recipe
                        MachineState = EnumBEState.Sleeping; // go to sleep
                        StateChange();
                    }
                    if (RecipeProgress >= 1f)
                    {
                        // progress finished! 
                        ItemStack outputstack = currentPressRecipe.Outputs[0].ResolvedItemstack.Clone();
                        if (HasRoomInOutput(0))
                        {
                            // output is empty! need a new stack
                            // Api.World.GetItem(craftingCode)
                            if (OutputSlot.Empty)
                            {
                                Inventory[1].Itemstack = outputstack;
                            }
                            else
                            {
                                int capleft = Inventory[1].Itemstack.Collectible.MaxStackSize - Inventory[1].Itemstack.StackSize;

                                if (capleft <= 0) Api.World.SpawnItemEntity(outputstack, Pos.UpCopy(1).ToVec3d());
                                else if (capleft >= outputstack.StackSize) Inventory[1].Itemstack.StackSize += outputstack.StackSize;
                                else
                                {
                                    Inventory[1].Itemstack.StackSize += capleft;
                                    outputstack.StackSize -= capleft;
                                    Api.World.SpawnItemEntity(outputstack, Pos.UpCopy(1).ToVec3d());
                                }

                            }
                            OutputSlot.MarkDirty();
                        }
                        else
                        {
                            // no room in main output, how'd we get in here, machine should stop when full...
                            Api.World.SpawnItemEntity(outputstack, Pos.UpCopy(1).ToVec3d());
                        }
                        if (currentPressRecipe.Outputs.Length > 1)
                        {
                            // this recipe has a second output
                            int varoutput = currentPressRecipe.Outputs[1].VariableResolve(Api.World, "VintEng: Metal Press Craft output");
                            if (varoutput > 0)
                            {
                                // depending on Variable set in output stacksize COULD be 0.
                                ItemStack extraoutputstack = new ItemStack(Api.World.GetItem(currentPressRecipe.Outputs[1].ResolvedItemstack.Collectible.Code),
                                                                           varoutput);
                                if (extraoutputstack.StackSize > 0 && HasRoomInOutput(1))
                                {
                                    if (ExtraOutputSlot.Empty)
                                    {
                                        Inventory[2].Itemstack = extraoutputstack.Clone();
                                    }
                                    else
                                    {
                                        // drop extras on the ground
                                        int capremaining = Inventory[2].Itemstack.Collectible.MaxStackSize - Inventory[2].Itemstack.StackSize;
                                        if (capremaining >= extraoutputstack.StackSize)
                                        {
                                            Inventory[2].Itemstack.StackSize += extraoutputstack.StackSize;
                                        }
                                        else
                                        {
                                            Inventory[2].Itemstack.StackSize += capremaining;
                                            extraoutputstack.StackSize -= capremaining;
                                            Api.World.SpawnItemEntity(extraoutputstack, Pos.UpCopy(1).ToVec3d());
                                            // spawn what we can't fit
                                        }
                                    }
                                }
                                else
                                {
                                    // no room in output, drop on ground
                                    // TODO Drop in FRONT of the block, or some predetermined place.
                                    Api.World.SpawnItemEntity(extraoutputstack, this.Pos.UpCopy(1).ToVec3d());
                                }
                                ExtraOutputSlot.MarkDirty();
                            }
                        }                                            

                        // damage the mold...
                        if (!MoldSlot.Empty && currentPressRecipe.RequiresDurability) // let the recipe control whether durability is used
                        {
                            string moldmetal = "game:metalbit-" + MoldSlot.Itemstack.Collectible.LastCodePart();
                            int molddur = MoldSlot.Itemstack.Collectible.GetRemainingDurability(MoldSlot.Itemstack);
                            molddur -= 1;
                            MoldSlot.Itemstack.Attributes.SetInt("durability", molddur);
                            if (molddur == 0)
                            {
                                if (Api.Side == EnumAppSide.Server)
                                {
                                    AssetLocation thebits = new AssetLocation(moldmetal);
                                    int newstack = Api.World.Rand.Next(5, 16);
                                    ItemStack bitstack = new ItemStack(Api.World.GetItem(thebits), newstack);
                                    Api.World.SpawnItemEntity(bitstack, Pos.UpCopy().ToVec3d(), null);
                                }
                                MoldSlot.Itemstack = null; // NO SOUP FOR YOU                                
                                Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/toolbreak"),
                                    this.Pos.X, this.Pos.Y, this.Pos.Z, null, 1f, 16f, 1f);
                            }
                            MoldSlot.MarkDirty();
                        }
                        // remove used ingredients from input
                        InputSlot.TakeOut(currentPressRecipe.Ingredients[0].Quantity);
                        InputSlot.MarkDirty();

                        if (!FindMatchingRecipe())
                        {
                            MachineState = EnumBEState.Sleeping;
                            isCrafting = false;
                            StateChange();
                        }
                        recipePowerApplied = 0;
                        MarkDirty(true, null);
                        Api.World.BlockAccessor.MarkBlockEntityDirty(this.Pos);
                    }
                }
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server)
            {
                sapi = api as ICoreServerAPI;
            }
            else
            {
                capi = api as ICoreClientAPI;
                if (AnimUtil != null)
                {
                    AnimUtil.InitializeAnimator("vemetalpress", null, null, new Vec3f(0f, GetRotation(), 0f) );
                }
                UpdateMesh(3);
            }
            this.inventory.Pos = this.Pos;
            this.inventory.LateInitialize($"{InventoryClassName}-{this.Pos.X}/{this.Pos.Y}/{this.Pos.Z}", api);
            this.RegisterGameTickListener(new Action<float>(OnSimTick), 100, 0);
        }

        public override void StateChange()
        {
            if (MachineState == EnumBEState.On)
            {
                if (AnimUtil != null)
                {
                    AnimUtil.StartAnimation(new AnimationMetaData
                    {
                        Animation = base.Block.Attributes["craftinganimcode"].AsString(),
                        Code = base.Block.Attributes["craftinganimcode"].AsString(),
                        AnimationSpeed = 1f,
                        EaseOutSpeed = 4f,
                        EaseInSpeed = 1f
                    });
                }
            }
            else
            {
                if (AnimUtil != null)
                {                    
                    AnimUtil.StopAnimation("craft");
                }
            }
            if (Api != null && Api.Side == EnumAppSide.Client && clientDialog != null && clientDialog.IsOpened())
            {
                clientDialog.Update(RecipeProgress, CurrentPower, currentPressRecipe);
            }
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (this.Api.Side == EnumAppSide.Client)
            {
                base.toggleInventoryDialogClient(byPlayer, delegate
                {
                    this.clientDialog = new GUIMetalPress(DialogTitle, Inventory, this.Pos, this.Api as ICoreClientAPI, this);
                    this.clientDialog.Update(RecipeProgress, CurrentPower, currentPressRecipe);
                    return this.clientDialog;
                });
            }
            return true;
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(player, packetid, data);
            if (packetid == 1002) // Enable button pressed
            {
                if (IsEnabled) // we're enabled, we need to turn off
                {
                    MachineState = EnumBEState.Off;
                }
                else
                {
                    MachineState = isCrafting ? EnumBEState.On : EnumBEState.Sleeping;
                }
                MarkDirty(true, null);
                StateChange();
            }    
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            base.OnReceivedServerPacket(packetid, data);
            if (clientDialog != null && clientDialog.IsOpened()) clientDialog.Update(RecipeProgress, CurrentPower, currentPressRecipe);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute invtree = new TreeAttribute();
            this.inventory.ToTreeAttributes(invtree);
            tree["inventory"] = invtree;

            tree.SetLong("recipepowerapplied", (long)recipePowerApplied);
            
            tree.SetBool("isCrafting", isCrafting);            
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            this.inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
            if (Api != null) Inventory.AfterBlocksLoaded(this.Api.World);
            recipePowerApplied = (ulong)tree.GetLong("recipepowerapplied");            
            isCrafting = tree.GetBool("isCrafting");            

            FindMatchingRecipe();
            if (Api != null && Api.Side == EnumAppSide.Client)
            {                
                if (this.clientDialog != null)
                {
                    clientDialog.Update(RecipeProgress, CurrentPower, currentPressRecipe);                    
                }
                UpdateMesh(3);
                MarkDirty(true, null);
            }
        }
    }
}